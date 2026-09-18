namespace Mise.Modules.Tables.Domain;

/// <summary>
/// Charter §10's five values. Phase 7 makes every value reachable: <see cref="Reserved"/> is
/// still not driven by any command (the "upcoming within 30 minutes" flag from US-03/FR-04 is
/// deferred — see CLAUDE.md), but <see cref="Occupied"/>/<see cref="Available"/> now flow from
/// <see cref="Table.MarkOccupied"/>/<see cref="Table.ReleaseIfReservationHeld"/> (the seat/
/// release cross-module events), and every value including <see cref="Reserved"/> is directly
/// settable via <see cref="Table.SetStatus"/> (FR-06's staff override).
/// </summary>
public enum TableStatus
{
    Available,
    Reserved,
    Occupied,
    NeedsCleaning,
    Blocked,
}
