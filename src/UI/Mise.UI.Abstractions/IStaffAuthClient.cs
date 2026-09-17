namespace Mise.UI.Abstractions;

/// <summary>
/// One implementation (HTTP) satisfies this for both Mise.Web and, later, Mise.Maui —
/// deliberately its own types, not a reuse of Mise.Modules.StaffIdentity.Contracts (the RCL
/// must not know a module exists at all, CLAUDE.md's UI boundary row).
/// </summary>
public interface IStaffAuthClient
{
    Task<LoginResult> LoginAsync(string username, string password, CancellationToken cancellationToken);
}

public sealed record LoginResult(
    bool IsSuccess, Guid StaffUserId, string FullName, string Role, string Token, DateTimeOffset ExpiresAtUtc)
{
    public static LoginResult Succeeded(Guid staffUserId, string fullName, string role, string token, DateTimeOffset expiresAtUtc) =>
        new(true, staffUserId, fullName, role, token, expiresAtUtc);

    public static LoginResult Failed() => new(false, Guid.Empty, string.Empty, string.Empty, string.Empty, default);
}
