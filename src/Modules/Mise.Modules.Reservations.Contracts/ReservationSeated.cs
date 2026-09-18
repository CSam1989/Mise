using Mise.SharedKernel;

namespace Mise.Modules.Reservations.Contracts;

/// <summary>
/// ADR-007's first real cross-module domain event (CLAUDE.md) — the charter's own §10 example
/// ("Reservation Seated → Table Occupied"). Published by <c>SeatReservationCommandHandler</c>
/// after a successful persist, on every <c>ReservationSaveOutcome.Saved</c> outcome including a
/// replayed OperationId (not gated by <c>WasAlreadyProcessed</c> the way the audit write is):
/// unlike an audit entry, redispatching this is safe because <c>Table.MarkOccupied</c> is
/// idempotent, and redispatching is exactly the self-healing behavior wanted if the first
/// attempt's cross-module step failed after the Reservation committed but before the Table did.
/// Handled by <c>Mise.Modules.Tables.Application.ReservationSeatedTableOccupiedHandler</c>.
/// </summary>
public sealed record ReservationSeated(
    Guid ReservationId, Guid TableId, Guid PerformedByStaffId, DateTimeOffset OccurredAtUtc) : IDomainEvent;
