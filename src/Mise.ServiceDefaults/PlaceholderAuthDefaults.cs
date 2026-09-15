namespace Mise.ServiceDefaults;

/// <summary>
/// Phase 2's placeholder JWT-bearer scheme, shared by Mise.ApiService (validates) and
/// Mise.Web (mints a system-identity token for its own outgoing API calls) — replaced by
/// StaffIdentity's real sign-in flow in Phase 3 (ADR-004). Both hosts are given the same
/// signing key via Mise.AppHost's "jwt-signing-key" parameter.
/// </summary>
public static class PlaceholderAuthDefaults
{
    public const string Issuer = "mise-placeholder-auth";
    public const string Audience = "mise-api";
    public const string SigningKeyConfigKey = "Authentication:Jwt:SigningKey";
}
