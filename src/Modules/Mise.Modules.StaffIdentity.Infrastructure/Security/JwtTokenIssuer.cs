using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using Mise.Modules.StaffIdentity.Application.Ports;
using Mise.Modules.StaffIdentity.Domain;
using Mise.ServiceDefaults;

namespace Mise.Modules.StaffIdentity.Infrastructure.Security;

/// <summary>
/// Real per-staff token minting — replaces Mise.Web's PlaceholderAuthTokenHandler (Phase 2).
/// Issues against the same Issuer/Audience/signing key Mise.ApiService already validates
/// (JwtAuthDefaults, ex-PlaceholderAuthDefaults) — CLAUDE.md's "the validation side stays,
/// only the minting side moves" note. A shift-length lifetime (8 hours): long enough not to
/// force a re-login mid-shift, short enough that a lost session doesn't stay valid for days.
/// </summary>
internal sealed class JwtTokenIssuer(string signingKey, TimeProvider timeProvider) : IJwtTokenIssuer
{
    private static readonly TimeSpan TokenLifetime = TimeSpan.FromHours(8);

    public IssuedToken IssueToken(Guid staffUserId, string fullName, StaffRole role)
    {
        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)), SecurityAlgorithms.HmacSha256);

        var now = timeProvider.GetUtcNow();
        var expiresAtUtc = now.Add(TokenLifetime);

        var token = new JwtSecurityToken(
            issuer: JwtAuthDefaults.Issuer,
            audience: JwtAuthDefaults.Audience,
            claims:
            [
                new Claim(ClaimTypes.NameIdentifier, staffUserId.ToString()),
                new Claim(ClaimTypes.Name, fullName),
                new Claim(ClaimTypes.Role, role.ToString()),
            ],
            notBefore: now.UtcDateTime,
            expires: expiresAtUtc.UtcDateTime,
            signingCredentials: credentials);

        return new IssuedToken(new JwtSecurityTokenHandler().WriteToken(token), expiresAtUtc);
    }
}
