namespace Mise.ApiService.Reservations;

internal sealed record CreateReservationRequest(
    Guid OperationId,
    string CustomerName,
    string CustomerPhone,
    int PartySize,
    DateTimeOffset ReservationDateTime,
    string? CustomerEmail = null,
    int? DurationMinutes = null,
    Guid? TableId = null,
    string? Notes = null);
