namespace Mise.Modules.Reservations.Application.SeatReservation;

/// <summary>FR-05's "assign a reservation to a specific table and mark the party as seated" —
/// <see cref="TableId"/> is always applied, even when it matches the table already on the
/// reservation, so a caller can reassign at seat time without a separate Update call first.</summary>
public sealed record SeatReservationCommand(
    Guid OperationId, Guid ReservationId, uint ExpectedVersion, Guid TableId, Guid PerformedByStaffId);
