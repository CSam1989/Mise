namespace Mise.Modules.Reservations.Application.MarkReservationNoShow;

public sealed record MarkReservationNoShowCommand(
    Guid OperationId, Guid ReservationId, uint ExpectedVersion, Guid PerformedByStaffId);
