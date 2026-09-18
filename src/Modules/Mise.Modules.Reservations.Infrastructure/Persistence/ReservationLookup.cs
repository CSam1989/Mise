using Microsoft.EntityFrameworkCore;
using Mise.Modules.Reservations.Contracts;
using Mise.Modules.Reservations.Domain;

namespace Mise.Modules.Reservations.Infrastructure.Persistence;

/// <summary>
/// Implements this module's own Contracts interface (ADR-007, mirroring ADR-006's
/// <c>TableAvailabilityLookup</c> in the opposite direction) — allowed by
/// ModuleBoundaryTests.Infrastructure_IsReferencedByNothingExceptTheCompositionRoot, which
/// forbids another module's Domain/Application/Contracts from referencing THIS module's
/// Infrastructure, not the reverse. Registered in DI at Mise.ApiService, consumed by
/// Mise.Modules.Tables.Application's ReservationTableVacatedHandler.
/// </summary>
internal sealed class ReservationLookup(ReservationsDbContext dbContext) : IReservationLookup
{
    /// <summary>Same "pull a small, indexed candidate set, then finish the exact arithmetic in
    /// memory" approach as <c>ReservationsData.HasOverlapAsync</c> — DurationMinutes addition
    /// isn't reliably EF/Npgsql-translatable inside a LINQ predicate, and the TableId index keeps
    /// the candidate set small regardless.</summary>
    public async Task<bool> HasCurrentActiveReservationForTableAsync(
        Guid tableId, Guid excludingReservationId, DateTimeOffset asOfUtc, CancellationToken cancellationToken)
    {
        var candidates = await dbContext.Reservations.AsNoTracking()
            .Where(r => r.TableId == tableId
                && r.Id != excludingReservationId
                && (r.Status == ReservationStatus.Confirmed || r.Status == ReservationStatus.Seated)
                && r.ReservationDateTime <= asOfUtc
                && r.ReservationDateTime >= asOfUtc.AddDays(-1))
            .Select(r => new { r.ReservationDateTime, r.DurationMinutes })
            .ToListAsync(cancellationToken);

        return candidates.Any(c => asOfUtc < c.ReservationDateTime.AddMinutes(c.DurationMinutes));
    }
}
