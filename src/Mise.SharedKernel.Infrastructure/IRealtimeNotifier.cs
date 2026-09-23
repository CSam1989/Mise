namespace Mise.SharedKernel.Infrastructure;

/// <summary>
/// Phase 8 (FR-10, NFR-04, US-03) — the frozen, transport-agnostic contract for pushing a
/// live-propagation event to every connected staff device. Constructor-injected directly into
/// command handlers (and the ADR-007 cross-module event handlers) and called once per real
/// effect, exactly like <see cref="IAuditWriter"/> — deliberately *not* routed through
/// <see cref="IDomainEventPublisher"/>: that mechanism exists for one module reacting to
/// another's business event (ADR-005/007), and a generic "relay this over SignalR" concern has
/// no natural owning module to react from. Skipped on a replayed <c>OperationId</c> (unlike the
/// ADR-007 cross-module dispatch, which deliberately redispatches on replay to retry a possibly
/// -failed side effect): nothing changed on a replay, so there is nothing new for a connected
/// client to learn — see CLAUDE.md's "Real-time propagation" section (ADR-008) for the full
/// reasoning. The one implementation (<c>Mise.ApiService.Realtime.SignalRRealtimeNotifier</c>)
/// swallows and logs a broadcast failure rather than letting it fail the triggering mutation —
/// a missed live-update push is a staleness/UX concern, never a data-integrity one, so it must
/// not turn an already-successful write into a 500.
/// </summary>
public interface IRealtimeNotifier
{
    Task NotifyTableStatusChangedAsync(TableStatusChangedNotification notification, CancellationToken cancellationToken);

    Task NotifyReservationCreatedAsync(ReservationChangedNotification notification, CancellationToken cancellationToken);

    Task NotifyReservationUpdatedAsync(ReservationChangedNotification notification, CancellationToken cancellationToken);

    Task NotifyReservationCancelledAsync(ReservationChangedNotification notification, CancellationToken cancellationToken);
}

/// <summary>
/// FR-06's direct staff override and both ADR-007 cross-module handlers (seat → Occupied,
/// vacate → Released/unchanged) all funnel through the same shape — the wire event only needs
/// to say *what changed*, not *why*; a client that wants more re-fetches the floor plan.
/// </summary>
public sealed record TableStatusChangedNotification(Guid TableId, string Status);

/// <summary>
/// Shared by Create/Update/Cancel/Seat/NoShow — deliberately its own shape, not a reuse of any
/// module's Contracts DTO (<c>Mise.SharedKernel.Infrastructure</c> sits below every module and
/// must stay module-agnostic, the same reasoning <c>Mise.UI.Abstractions.ReservationDto</c>'s own
/// doc comment gives for not reusing <c>Mise.Modules.Reservations.Contracts.ReservationDto</c>).
/// Seat/NoShow both notify as <see cref="IRealtimeNotifier.NotifyReservationUpdatedAsync"/> —
/// ADR-008 collapses the charter's undeclared per-transition events into the hub's own declared,
/// frozen four (<c>TableStatusChanged</c>, <c>ReservationCreated</c>, <c>ReservationUpdated</c>,
/// <c>ReservationCancelled</c>), status being just another field a client reads off the payload.
/// </summary>
public sealed record ReservationChangedNotification(
    Guid ReservationId,
    string CustomerName,
    int PartySize,
    DateTimeOffset ReservationDateTime,
    string Status,
    Guid? TableId);
