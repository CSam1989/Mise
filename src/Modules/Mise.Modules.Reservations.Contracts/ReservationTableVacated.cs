using Mise.SharedKernel;

namespace Mise.Modules.Reservations.Contracts;

/// <summary>
/// BR-05's "cancelling or no-showing a reservation frees its table" — published by both
/// <c>CancelReservationCommandHandler</c> and <c>MarkReservationNoShowCommandHandler</c> (same
/// shape, same downstream handling either way) whenever the reservation being cancelled/
/// no-showed had a <see cref="TableId"/> assigned. Same replay semantics as
/// <see cref="ReservationSeated"/> — dispatched on every Saved outcome, not gated by
/// WasAlreadyProcessed, because <c>Table.ReleaseIfReservationHeld</c> is idempotent. Handled by
/// <c>Mise.Modules.Tables.Application.ReservationTableVacatedHandler</c>, which decides the
/// "unless another active reservation holds it" half of BR-05 via
/// <see cref="IReservationLookup"/> before touching the table.
/// </summary>
public sealed record ReservationTableVacated(
    Guid ReservationId, Guid TableId, Guid PerformedByStaffId, DateTimeOffset OccurredAtUtc) : IDomainEvent;
