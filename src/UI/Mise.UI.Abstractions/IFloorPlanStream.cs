namespace Mise.UI.Abstractions;

/// <summary>
/// Phase 8's frozen real-time contract (FR-10/NFR-04/US-03) — lands now, ahead of any concrete
/// implementation or UI consumer, for the same reason <c>OperationId</c> idempotency landed in
/// Phase 2 before the offline Outbox needed it: MAUI's later sync/live-floor-plan work depends on
/// this exact shape existing, and retrofitting a wire contract after clients are already built
/// against it is a breaking change, not a refactor. One implementation (HTTP+SignalR, ADR-004)
/// will satisfy this for both Mise.Web and Mise.Maui, the same way one implementation already
/// satisfies <see cref="IReservationsClient"/> — deliberately its own duplicated payload shapes,
/// not a reuse of any module's Contracts DTO (this project's own zero-package, zero-reference
/// invariant is what lets the shared RCL render under both hosts without knowing a module
/// exists — see this project's .csproj comment and CLAUDE.md's UI boundary row). The concrete
/// client and its consuming screen land together in Phase 10, once the shared RCL's floor-plan
/// board exists to drive with it — an abstraction with no consumer yet is exactly Phase 6's
/// still-unused <c>ReservationCreated</c> event's situation, not a gap unique to this one.
/// </summary>
public interface IFloorPlanStream : IAsyncDisposable
{
    Task StartAsync(CancellationToken cancellationToken);

    event Action<TableStatusChangedPayload>? TableStatusChanged;

    event Action<ReservationChangedPayload>? ReservationCreated;

    event Action<ReservationChangedPayload>? ReservationUpdated;

    event Action<ReservationChangedPayload>? ReservationCancelled;
}

public sealed record TableStatusChangedPayload(Guid TableId, string Status);

public sealed record ReservationChangedPayload(
    Guid ReservationId,
    string CustomerName,
    int PartySize,
    DateTimeOffset ReservationDateTime,
    string Status,
    Guid? TableId);
