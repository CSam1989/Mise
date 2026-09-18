namespace Mise.Modules.Reservations.Contracts;

/// <summary>
/// ADR-007's reverse-direction counterpart to <c>Mise.Modules.Tables.Contracts.ITableAvailabilityLookup</c>
/// (ADR-006) — same pattern, opposite ownership: this module answers a question only it can
/// answer (which reservations currently hold a table), consumed by
/// <c>Mise.Modules.Tables.Application</c>'s <c>ReservationTableVacatedHandler</c> to decide BR-05's
/// "unless another active reservation holds it" clause. Implemented by
/// <c>Mise.Modules.Reservations.Infrastructure</c>, registered in DI at Mise.ApiService.
/// </summary>
public interface IReservationLookup
{
    /// <summary>True when a Confirmed or Seated reservation other than
    /// <paramref name="excludingReservationId"/> is assigned to <paramref name="tableId"/> and
    /// its window (<c>[ReservationDateTime, ReservationDateTime + DurationMinutes)</c>) covers
    /// <paramref name="asOfUtc"/> — i.e. it currently, actively holds the table right now, not
    /// merely at some other point in the day. BR-05 only cares about the table's *current*
    /// status, so a same-table reservation later today must not block releasing it now.</summary>
    Task<bool> HasCurrentActiveReservationForTableAsync(
        Guid tableId, Guid excludingReservationId, DateTimeOffset asOfUtc, CancellationToken cancellationToken);
}
