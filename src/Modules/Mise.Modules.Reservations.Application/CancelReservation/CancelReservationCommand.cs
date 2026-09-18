namespace Mise.Modules.Reservations.Application.CancelReservation;

public sealed record CancelReservationCommand(
    Guid OperationId, Guid ReservationId, uint ExpectedVersion, Guid PerformedByStaffId);
