namespace Mise.Modules.Reservations.Application.CreateReservation;

/// <summary>
/// <paramref name="PerformedBy"/> is whatever the caller's authenticated identity resolves
/// to today (docs/plan.md's Phase 2 placeholder auth spine names a system process, not a
/// staff member — StaffIdentity in Phase 3 starts passing a real staff id here instead).
/// </summary>
public sealed record CreateReservationCommand(
    Guid OperationId,
    string CustomerName,
    int PartySize,
    DateTimeOffset ReservationDateTime,
    string PerformedBy);
