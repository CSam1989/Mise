using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Mise.Modules.Reservations.Application.Ports;
using Mise.Modules.Reservations.Domain;
using Mise.SharedKernel.Persistence;
using Npgsql;

namespace Mise.Modules.Reservations.Infrastructure.Persistence;

/// <summary>
/// The "Version" shadow property (see ReservationConfiguration's doc comment) is read/written
/// here exclusively, same as Tables' gateway. BR-01 (docs/plan.md correction #1) is enforced
/// twice, deliberately: <see cref="HasOverlapAsync"/> is a friendly, in-memory pre-check over a
/// small candidate set (so most callers get a clean result without ever touching the
/// constraint), and the Postgres exclusion constraint itself is the TOCTOU-safe authority —
/// caught here as a <see cref="PostgresException"/> with SqlState 23P01 when two concurrent
/// requests both pass the pre-check and race to insert/update.
/// </summary>
internal sealed partial class ReservationsData(
    ReservationsDbContext dbContext, TimeProvider timeProvider, ILogger<ReservationsData> logger)
    : IReservationsData
{
    public async Task<ReservationSaveResult> CreateReservationAsync(
        Reservation reservation, Guid operationId, CancellationToken cancellationToken)
    {
        var existingResourceId = await FindProcessedResourceIdAsync(operationId, cancellationToken);
        if (existingResourceId is { } resourceId)
        {
            LogIdempotencyCheckHit(operationId, resourceId);
            var existing = await ReadCurrentAsync(resourceId, cancellationToken);
            return new ReservationSaveResult(ReservationSaveOutcome.Saved, existing.Reservation, existing.Version, WasAlreadyProcessed: true);
        }

        if (await HasOverlapAsync(reservation, cancellationToken))
        {
            LogTableOverlapPreCheck(reservation.Id);
            return new ReservationSaveResult(ReservationSaveOutcome.TableOverlap, reservation, Version: 0, WasAlreadyProcessed: false);
        }

        dbContext.Reservations.Add(reservation);
        RecordProcessedOperation(operationId, reservation.Id);

        if (!await TrySaveChangesAsync(cancellationToken))
        {
            LogTableOverlapAtSave(reservation.Id);
            return new ReservationSaveResult(ReservationSaveOutcome.TableOverlap, reservation, Version: 0, WasAlreadyProcessed: false);
        }

        return new ReservationSaveResult(ReservationSaveOutcome.Saved, reservation, CurrentVersionOf(reservation), WasAlreadyProcessed: false);
    }

    public async Task<ReservationWithVersion?> GetReservationByIdAsync(Guid reservationId, CancellationToken cancellationToken)
    {
        var reservation = await dbContext.Reservations.SingleOrDefaultAsync(r => r.Id == reservationId, cancellationToken);
        if (reservation is null)
        {
            return null;
        }

        return new ReservationWithVersion(reservation, CurrentVersionOf(reservation));
    }

    public Task<ReservationSaveResult> UpdateReservationAsync(
        Reservation reservation, uint expectedVersion, Guid operationId, CancellationToken cancellationToken) =>
        SaveWithConcurrencyCheckAsync(reservation, expectedVersion, operationId, checkOverlap: true, cancellationToken);

    public Task<ReservationSaveResult> CancelReservationAsync(
        Reservation reservation, uint expectedVersion, Guid operationId, CancellationToken cancellationToken) =>
        SaveWithConcurrencyCheckAsync(reservation, expectedVersion, operationId, checkOverlap: false, cancellationToken);

    public async Task<IReadOnlyList<ReservationWithVersion>> SearchReservationsAsync(
        string? query, DateOnly? date, CancellationToken cancellationToken)
    {
        var reservations = dbContext.Reservations.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(query))
        {
            // ILIKE with a leading wildcard can't use a plain B-tree index, but it can use the
            // pg_trgm GIN index the migration adds on CustomerName (charter §10's Key Indexes) —
            // CustomerPhone has no such index yet (plain B-tree only), so a partial-phone search
            // here is an unindexed scan; revisit if phone search volume ever makes that matter.
            var pattern = $"%{query}%";
            reservations = reservations.Where(r => EF.Functions.ILike(r.CustomerName, pattern) || EF.Functions.ILike(r.CustomerPhone, pattern));
        }

        if (date is { } d)
        {
            var startOfDay = new DateTimeOffset(d.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            var endOfDay = startOfDay.AddDays(1);
            reservations = reservations.Where(r => r.ReservationDateTime >= startOfDay && r.ReservationDateTime < endOfDay);
        }

        var rows = await reservations
            .OrderBy(r => r.ReservationDateTime)
            .Select(r => new { Reservation = r, Version = EF.Property<uint>(r, "Version") })
            .ToListAsync(cancellationToken);

        return rows.Select(r => new ReservationWithVersion(r.Reservation, r.Version)).ToList();
    }

    private async Task<ReservationSaveResult> SaveWithConcurrencyCheckAsync(
        Reservation reservation, uint expectedVersion, Guid operationId, bool checkOverlap, CancellationToken cancellationToken)
    {
        var existingResourceId = await FindProcessedResourceIdAsync(operationId, cancellationToken);
        if (existingResourceId is not null)
        {
            LogIdempotencyCheckHit(operationId, reservation.Id);
            return new ReservationSaveResult(ReservationSaveOutcome.Saved, reservation, CurrentVersionOf(reservation), WasAlreadyProcessed: true);
        }

        if (checkOverlap && await HasOverlapAsync(reservation, cancellationToken))
        {
            LogTableOverlapPreCheck(reservation.Id);
            return new ReservationSaveResult(ReservationSaveOutcome.TableOverlap, reservation, CurrentVersionOf(reservation), WasAlreadyProcessed: false);
        }

        dbContext.Entry(reservation).Property<uint>("Version").OriginalValue = expectedVersion;
        RecordProcessedOperation(operationId, reservation.Id);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            LogVersionMismatch(reservation.Id, expectedVersion);
            var current = await ReadCurrentAsync(reservation.Id, cancellationToken);
            return new ReservationSaveResult(ReservationSaveOutcome.VersionMismatch, current.Reservation, current.Version, WasAlreadyProcessed: false);
        }
        catch (DbUpdateException ex) when (IsExclusionViolation(ex))
        {
            LogTableOverlapAtSave(reservation.Id);
            return new ReservationSaveResult(ReservationSaveOutcome.TableOverlap, reservation, CurrentVersionOf(reservation), WasAlreadyProcessed: false);
        }

        return new ReservationSaveResult(ReservationSaveOutcome.Saved, reservation, CurrentVersionOf(reservation), WasAlreadyProcessed: false);
    }

    /// <summary>Bounded to a +/-1 day window so this stays a cheap, indexed lookup (charter
    /// §10's ReservationDateTime index) rather than scanning a table's entire history — the
    /// exact overlap arithmetic (which needs DurationMinutes, not reliably translatable inside
    /// an EF/Npgsql LINQ predicate) then runs in memory over this small candidate set. This is
    /// purely the friendly pre-check; the Postgres exclusion constraint is what's actually
    /// authoritative regardless of any gap in this method's own reach.</summary>
    private async Task<bool> HasOverlapAsync(Reservation reservation, CancellationToken cancellationToken)
    {
        if (reservation.TableId is not { } tableId)
        {
            return false;
        }

        var newStart = reservation.ReservationDateTime;
        var newEnd = newStart.AddMinutes(reservation.DurationMinutes);

        var candidates = await dbContext.Reservations.AsNoTracking()
            .Where(r => r.Id != reservation.Id
                && r.TableId == tableId
                && r.Status != ReservationStatus.Cancelled
                && r.Status != ReservationStatus.NoShow
                && r.ReservationDateTime >= newStart.AddDays(-1)
                && r.ReservationDateTime <= newStart.AddDays(1))
            .Select(r => new { r.ReservationDateTime, r.DurationMinutes })
            .ToListAsync(cancellationToken);

        return candidates.Any(c => newStart < c.ReservationDateTime.AddMinutes(c.DurationMinutes) && c.ReservationDateTime < newEnd);
    }

    private async Task<bool> TrySaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException ex) when (IsExclusionViolation(ex))
        {
            return false;
        }
    }

    private static bool IsExclusionViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.ExclusionViolation };

    private async Task<ReservationWithVersion> ReadCurrentAsync(Guid reservationId, CancellationToken cancellationToken)
    {
        var row = await dbContext.Reservations.AsNoTracking()
            .Where(r => r.Id == reservationId)
            .Select(r => new { Reservation = r, Version = EF.Property<uint>(r, "Version") })
            .SingleAsync(cancellationToken);
        return new ReservationWithVersion(row.Reservation, row.Version);
    }

    private uint CurrentVersionOf(Reservation reservation) => dbContext.Entry(reservation).Property<uint>("Version").CurrentValue;

    private async Task<Guid?> FindProcessedResourceIdAsync(Guid operationId, CancellationToken cancellationToken) =>
        await dbContext.ProcessedOperations.AsNoTracking()
            .Where(p => p.OperationId == operationId)
            .Select(p => (Guid?)p.ResourceId)
            .SingleOrDefaultAsync(cancellationToken);

    private void RecordProcessedOperation(Guid operationId, Guid resourceId) =>
        dbContext.ProcessedOperations.Add(new ProcessedOperation
        {
            OperationId = operationId,
            ResourceType = "Reservation",
            ResourceId = resourceId,
            ProcessedAtUtc = timeProvider.GetUtcNow(),
        });

    [LoggerMessage(EventId = 10, Level = LogLevel.Debug,
        Message = "OperationId {OperationId} already recorded in shared.processed_operation, pointing at resource {ResourceId}.")]
    private partial void LogIdempotencyCheckHit(Guid operationId, Guid resourceId);

    [LoggerMessage(EventId = 11, Level = LogLevel.Debug,
        Message = "BR-01 pre-check rejected reservation {ReservationId}'s table assignment before ever reaching Postgres.")]
    private partial void LogTableOverlapPreCheck(Guid reservationId);

    [LoggerMessage(EventId = 12, Level = LogLevel.Warning,
        Message = "BR-01 exclusion constraint rejected reservation {ReservationId}'s table assignment at save time — the pre-check missed a genuine race.")]
    private partial void LogTableOverlapAtSave(Guid reservationId);

    [LoggerMessage(EventId = 13, Level = LogLevel.Debug,
        Message = "Reservation {ReservationId} save rejected by Postgres: caller's xmin {ExpectedVersion} no longer matches the current row.")]
    private partial void LogVersionMismatch(Guid reservationId, uint expectedVersion);
}
