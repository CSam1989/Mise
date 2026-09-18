namespace Mise.Modules.Reservations.Domain;

/// <summary>
/// <see cref="Cancelled"/> is added in Phase 6 (<see cref="Reservation.Cancel"/>) — the only
/// other status FR-03 ("edit or cancel") needs. Phase 7 makes <see cref="Seated"/>
/// (<see cref="Reservation.MarkSeated"/>, FR-05/BR-04) and <see cref="NoShow"/>
/// (<see cref="Reservation.MarkNoShow"/>, BR-05) reachable too. <see cref="Completed"/> stays
/// unreachable — declared because the charter's data model names all five up front, same as
/// <c>TableStatus</c> already does in the Tables module, but no command drives it yet.
/// </summary>
public enum ReservationStatus
{
    Confirmed,
    Seated,
    Completed,
    NoShow,
    Cancelled,
}
