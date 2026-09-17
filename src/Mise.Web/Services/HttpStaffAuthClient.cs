using System.Net.Http.Json;
using System.Text.Json;
using Mise.UI.Abstractions;

namespace Mise.Web.Services;

internal sealed class HttpStaffAuthClient(HttpClient httpClient) : IStaffAuthClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<LoginResult> LoginAsync(string username, string password, CancellationToken cancellationToken)
    {
        var response = await httpClient.PostAsJsonAsync(
            "/api/auth/login", new { Username = username, Password = password }, JsonOptions, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            // 401 (wrong credentials/inactive account) and 400 (empty fields — the login page
            // already requires both via DataAnnotationsValidator, so this shouldn't normally
            // happen) both surface the same way here: the page shows one generic message
            // either way, deliberately not distinguishing why.
            return LoginResult.Failed();
        }

        var payload = await response.Content.ReadFromJsonAsync<LoginResponsePayload>(JsonOptions, cancellationToken)
            ?? throw new InvalidOperationException("The API returned an empty success response.");

        return LoginResult.Succeeded(payload.StaffUserId, payload.FullName, payload.Role, payload.Token, payload.ExpiresAtUtc);
    }

    private sealed record LoginResponsePayload(
        string Token, DateTimeOffset ExpiresAtUtc, Guid StaffUserId, string FullName, string Role);
}
