using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using Mise.ServiceDefaults;

namespace Mise.Web.Services;

/// <summary>
/// Mints a fixed system-identity JWT and attaches it to every outgoing API call — the
/// Phase 2 stand-in for ADR-004's real sign-in flow (a server-side JWT scoped to the signed-in
/// user's circuit), which lands in Phase 3. There is no per-user identity yet, so every
/// request from this host authenticates as the same "Mise.Web" system process — see
/// CreateReservationCommand's PerformedBy field.
/// </summary>
internal sealed partial class PlaceholderAuthTokenHandler(
    IConfiguration configuration, ILogger<PlaceholderAuthTokenHandler> logger) : DelegatingHandler
{
    private const string SystemProcessName = "Mise.Web";

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // Never log the token itself (CLAUDE.md's Logging rule: never log a secret).
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", MintToken());
        LogTokenAttached(request.RequestUri);
        return base.SendAsync(request, cancellationToken);
    }

    private string MintToken()
    {
        var signingKey = configuration[PlaceholderAuthDefaults.SigningKeyConfigKey]
            ?? throw new InvalidOperationException(
                $"Configuration key '{PlaceholderAuthDefaults.SigningKeyConfigKey}' is required.");

        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)), SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: PlaceholderAuthDefaults.Issuer,
            audience: PlaceholderAuthDefaults.Audience,
            claims: [new Claim(ClaimTypes.Name, SystemProcessName)],
            expires: DateTime.UtcNow.AddMinutes(5),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    [LoggerMessage(EventId = 40, Level = LogLevel.Trace, Message = "Attached a placeholder auth token to outgoing request {RequestUri}.")]
    private partial void LogTokenAttached(Uri? requestUri);
}
