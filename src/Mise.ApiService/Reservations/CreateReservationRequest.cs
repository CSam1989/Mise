namespace Mise.ApiService.Reservations;

internal sealed record CreateReservationRequest(
    Guid OperationId, string CustomerName, int PartySize, DateTimeOffset ReservationDateTime);
