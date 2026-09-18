namespace Mise.Modules.Reservations.Domain;

/// <summary>
/// <see cref="Cancelled"/> is added in Phase 6 (<see cref="Reservation.Cancel"/>) — the only
/// other status FR-03 ("edit or cancel") needs. <see cref="Seated"/>/<see cref="Completed"/>/
/// <see cref="NoShow"/> stay unreachable via any command until Phase 7's seating flow
/// (BR-04/BR-05) actually drives them — declared now because the charter's data model names
/// all five up front, same as <c>TableStatus</c> already does in the Tables module.
/// </summary>
public enum ReservationStatus
{
    Confirmed,
    Seated,
    Completed,
    NoShow,
    Cancelled,
}
