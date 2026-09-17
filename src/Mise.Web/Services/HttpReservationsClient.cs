using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Components.Authorization;
using Mise.UI.Abstractions;

namespace Mise.Web.Services;

/// <summary>
/// Attaches the signed-in staff member's API token itself, rather than via a
/// DelegatingHandler on the typed HttpClient (AddHttpMessageHandler-registered handlers are
/// resolved from IHttpClientFactory's own pooled/rotating internal scope, not the calling
/// circuit's DI scope — a scoped dependency like AuthenticationStateProvider injected there
/// would silently resolve the wrong instance). This class itself, as the typed client
/// AddHttpClient&lt;TClient,TImplementation&gt; constructs, IS built from the calling scope
/// each time, so AuthenticationStateProvider here resolves correctly.
/// </summary>
internal sealed class HttpReservationsClient(
    HttpClient httpClient, AuthenticationStateProvider authenticationStateProvider, IStaffSessionTokenCache tokenCache)
    : IReservationsClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<CreateReservationResult> CreateAsync(
        CreateReservationRequest request, CancellationToken cancellationToken)
    {
        await AttachBearerTokenAsync();

        var response = await httpClient.PostAsJsonAsync("/api/reservations", request, JsonOptions, cancellationToken);

        if (response.IsSuccessStatusCode)
        {
            var dto = await response.Content.ReadFromJsonAsync<ReservationDto>(JsonOptions, cancellationToken)
                ?? throw new InvalidOperationException("The API returned an empty success response.");
            return CreateReservationResult.Succeeded(dto);
        }

        if (response.StatusCode == HttpStatusCode.BadRequest)
        {
            var problem = await response.Content.ReadFromJsonAsync<ValidationProblemPayload>(JsonOptions, cancellationToken);
            return CreateReservationResult.Failed(problem?.Errors ?? new Dictionary<string, string[]>());
        }

        response.EnsureSuccessStatusCode();
        throw new InvalidOperationException("Unreachable — EnsureSuccessStatusCode always throws for a non-success status.");
    }

    private async Task AttachBearerTokenAsync()
    {
        var authState = await authenticationStateProvider.GetAuthenticationStateAsync();
        var staffIdClaim = authState.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        // No claim (not signed in) or an expired/missing cache entry both fall through to an
        // unauthenticated request — the API's global fallback policy rejects it with 401,
        // which is the correct outcome; there is no anonymous system-identity fallback
        // anymore (Phase 2's placeholder behavior).
        if (staffIdClaim is not null
            && Guid.TryParse(staffIdClaim, out var staffId)
            && tokenCache.TryGet(staffId, out var token))
        {
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
    }

    private sealed record ValidationProblemPayload([property: JsonPropertyName("errors")] Dictionary<string, string[]> Errors);
}
