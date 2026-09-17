using System.Collections.Concurrent;

namespace Mise.Web.Services;

/// <summary>Singleton — deliberately not per-circuit-scoped, so the token a login stored
/// during the static-SSR POST (before any circuit exists) is still there once the
/// interactive circuit for the redirected page starts up (a different DI scope entirely).</summary>
internal sealed class StaffSessionTokenCache(TimeProvider timeProvider) : IStaffSessionTokenCache
{
    private readonly ConcurrentDictionary<Guid, CachedToken> _tokensByStaffId = new();

    public void Store(Guid staffUserId, string token, DateTimeOffset expiresAtUtc) =>
        _tokensByStaffId[staffUserId] = new CachedToken(token, expiresAtUtc);

    public bool TryGet(Guid staffUserId, out string token)
    {
        if (_tokensByStaffId.TryGetValue(staffUserId, out var cached) && cached.ExpiresAtUtc > timeProvider.GetUtcNow())
        {
            token = cached.Token;
            return true;
        }

        _tokensByStaffId.TryRemove(staffUserId, out _);
        token = string.Empty;
        return false;
    }

    public void Remove(Guid staffUserId) => _tokensByStaffId.TryRemove(staffUserId, out _);

    private sealed record CachedToken(string Token, DateTimeOffset ExpiresAtUtc);
}
