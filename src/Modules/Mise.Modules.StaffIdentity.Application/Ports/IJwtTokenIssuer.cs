using Mise.Modules.StaffIdentity.Domain;

namespace Mise.Modules.StaffIdentity.Application.Ports;

/// <summary>
/// Mints the bearer token a signed-in staff member's client attaches to every subsequent API
/// call. An Application-defined port so LoginCommandHandler stays testable with a mock — the
/// concrete JWT library usage (System.IdentityModel.Tokens.Jwt, the signing key) is an
/// Infrastructure concern, same seam shape as IReservationsData.
/// </summary>
public interface IJwtTokenIssuer
{
    IssuedToken IssueToken(Guid staffUserId, string fullName, StaffRole role);
}

public sealed record IssuedToken(string Token, DateTimeOffset ExpiresAtUtc);
