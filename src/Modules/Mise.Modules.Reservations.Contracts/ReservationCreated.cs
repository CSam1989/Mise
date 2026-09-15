using Mise.SharedKernel;

namespace Mise.Modules.Reservations.Contracts;

/// <summary>Wire/event contract for a created reservation. No subscriber yet — Phase 7 adds the first cross-module handler (Seat → table Occupied builds on this pattern).</summary>
public sealed record ReservationCreated(
    Guid ReservationId,
    string CustomerName,
    int PartySize,
    DateTimeOffset ReservationDateTime,
    DateTimeOffset OccurredAtUtc) : IDomainEvent;
