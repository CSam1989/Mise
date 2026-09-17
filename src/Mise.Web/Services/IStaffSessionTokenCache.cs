namespace Mise.Web.Services;

/// <summary>
/// Holds each signed-in staff member's API JWT server-side, keyed by staff id (the same id
/// the sign-in cookie's NameIdentifier claim carries) — never the raw token itself reaches
/// the browser as a cookie or localStorage value (ADR-004). Keying by staff id rather than by
/// circuit id is what makes a lookup survive a circuit reconnect: the cookie (and therefore
/// the claim) outlives any one circuit, so a fresh circuit after a reconnect finds the same
/// cached token via the same claim. Known gap: if two browser tabs/devices sign the same
/// staff member in twice, the second Store() overwrites the first's token — acceptable for a
/// single-restaurant staff tool where one person is not expected to run concurrent sessions
/// under different tokens on purpose.
/// </summary>
public interface IStaffSessionTokenCache
{
    void Store(Guid staffUserId, string token, DateTimeOffset expiresAtUtc);

    bool TryGet(Guid staffUserId, out string token);

    void Remove(Guid staffUserId);
}
