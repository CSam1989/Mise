namespace Mise.ServiceDefaults;

/// <summary>
/// Shared JWT-bearer constants — Mise.ApiService validates against these, and (as of Phase 3)
/// Mise.Modules.StaffIdentity.Infrastructure's JwtTokenIssuer mints real per-staff tokens
/// against the same ones. Named PlaceholderAuthDefaults through Phase 2, when Mise.Web minted
/// a single fixed system-identity token instead of a real sign-in existing at all — renamed
/// here because CLAUDE.md's own rule is that a stale name is worse than none once the thing
/// it described stops being a placeholder (only the minting side changed; the validation
/// side — this class, Mise.ApiService's JWT-bearer setup — is exactly what Phase 2 already
/// documented as staying put). Both hosts are given the same signing key via
/// Mise.AppHost's "jwt-signing-key" parameter, never hardcoded.
/// </summary>
public static class JwtAuthDefaults
{
    public const string Issuer = "mise-auth";
    public const string Audience = "mise-api";
    public const string SigningKeyConfigKey = "Authentication:Jwt:SigningKey";
}
