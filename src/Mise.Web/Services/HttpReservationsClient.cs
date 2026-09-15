using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Mise.UI.Abstractions;

namespace Mise.Web.Services;

internal sealed class HttpReservationsClient(HttpClient httpClient) : IReservationsClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<CreateReservationResult> CreateAsync(
        CreateReservationRequest request, CancellationToken cancellationToken)
    {
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

    private sealed record ValidationProblemPayload([property: JsonPropertyName("errors")] Dictionary<string, string[]> Errors);
}
