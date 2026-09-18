namespace Mise.ApiService.Reservations;

internal sealed record UpdateReservationRequest(
    Guid OperationId,
    string CustomerName,
    string CustomerPhone,
    int PartySize,
    DateTimeOffset ReservationDateTime,
    int DurationMinutes,
    string? CustomerEmail = null,
    Guid? TableId = null,
    string? Notes = null);

internal sealed record CancelReservationRequest(Guid OperationId);

internal sealed record SeatReservationRequest(Guid OperationId, Guid TableId);

internal sealed record MarkReservationNoShowRequest(Guid OperationId);
