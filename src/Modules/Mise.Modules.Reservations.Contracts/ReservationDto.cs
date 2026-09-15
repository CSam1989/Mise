namespace Mise.Modules.Reservations.Contracts;

public sealed record ReservationDto(
    Guid Id, string CustomerName, int PartySize, DateTimeOffset ReservationDateTime, string Status);
