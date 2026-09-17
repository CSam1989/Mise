namespace Mise.SharedKernel;

/// <summary>
/// Thrown by an Application command handler when a gateway's xmin-backed optimistic
/// concurrency check reports that the caller's <c>If-Match</c> version is stale (charter
/// correction #5: "every mutating endpoint on a concurrency-tracked aggregate requires an
/// ETag/If-Match header ... stale value → 409 with the current state"). Lives in
/// Mise.SharedKernel rather than a module's own Application project because the exception has
/// to cross the Application → Mise.ApiService boundary the same way FluentValidation's
/// ValidationException already does — Mise.ApiService's ConcurrencyConflictExceptionHandler is
/// the one place that turns it into the 409, setting the response ETag header from
/// <see cref="CurrentVersion"/> so the caller can retry without a separate GET.
/// <see cref="CurrentState"/> is a flat field dictionary rather than a typed DTO: the
/// Application handler that throws this already has the Domain entity in scope (Application
/// may reference its own Domain) and builds the dictionary itself — Mise.SharedKernel must not
/// reference any module's Domain, so it can't accept a concrete entity type here.
/// </summary>
public sealed class ConcurrencyConflictException(
    string entityType, Guid entityId, uint currentVersion, IReadOnlyDictionary<string, object?> currentState)
    : Exception($"{entityType} '{entityId}' was modified by someone else since it was last read.")
{
    public string EntityType { get; } = entityType;
    public Guid EntityId { get; } = entityId;
    public uint CurrentVersion { get; } = currentVersion;
    public IReadOnlyDictionary<string, object?> CurrentState { get; } = currentState;
}
