using Mise.Modules.StaffIdentity.Domain;

namespace Mise.Modules.StaffIdentity.Application.Login;

public enum LoginOutcome
{
    Success,
    InvalidCredentials,
    AccountInactive,
}

public sealed record LoginResult(
    LoginOutcome Outcome, Guid StaffUserId, string FullName, StaffRole Role, string Token, DateTimeOffset ExpiresAtUtc)
{
    public static LoginResult Failed(LoginOutcome outcome) =>
        new(outcome, Guid.Empty, string.Empty, default, string.Empty, default);
}
