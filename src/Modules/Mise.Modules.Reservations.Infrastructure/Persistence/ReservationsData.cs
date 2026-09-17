using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Mise.Modules.Reservations.Application.Ports;
using Mise.Modules.Reservations.Domain;
using Mise.SharedKernel.Persistence;

namespace Mise.Modules.Reservations.Infrastructure.Persistence;

internal sealed partial class ReservationsData(
    ReservationsDbContext dbContext, TimeProvider timeProvider, ILogger<ReservationsData> logger)
    : IReservationsData
{
    public async Task<CreateReservationResult> CreateReservationAsync(
        Reservation reservation, Guid operationId, CancellationToken cancellationToken)
    {
        var existingResourceId = await dbContext.ProcessedOperations
            .AsNoTracking()
            .Where(p => p.OperationId == operationId)
            .Select(p => (Guid?)p.ResourceId)
            .SingleOrDefaultAsync(cancellationToken);

        if (existingResourceId is { } resourceId)
        {
            LogIdempotencyCheckHit(operationId, resourceId);
            return new CreateReservationResult(resourceId, WasAlreadyProcessed: true);
        }

        LogIdempotencyCheckMiss(operationId, reservation.Id);

        // Same SaveChanges call as the reservation insert, so a replayed OperationId and the
        // resource it created become visible together or not at all — no window where the
        // reservation exists but isn't yet recorded as processed (docs/plan.md's "must land
        // in Phase 2" OperationId guardrail). Genuinely concurrent duplicate submissions of
        // the same OperationId are not hardened against yet — that needs the same kind of
        // interleaved-transaction test BR-01's overlap constraint gets in Phase 6.
        dbContext.Reservations.Add(reservation);
        dbContext.ProcessedOperations.Add(new ProcessedOperation
        {
            OperationId = operationId,
            ResourceType = "Reservation",
            ResourceId = reservation.Id,
            ProcessedAtUtc = timeProvider.GetUtcNow(),
        });

        await dbContext.SaveChangesAsync(cancellationToken);

        return new CreateReservationResult(reservation.Id, WasAlreadyProcessed: false);
    }

    [LoggerMessage(EventId = 10, Level = LogLevel.Debug,
        Message = "OperationId {OperationId} already recorded in shared.processed_operation, pointing at resource {ResourceId}.")]
    private partial void LogIdempotencyCheckHit(Guid operationId, Guid resourceId);

    [LoggerMessage(EventId = 11, Level = LogLevel.Debug,
        Message = "OperationId {OperationId} not previously recorded; inserting new reservation {ReservationId}.")]
    private partial void LogIdempotencyCheckMiss(Guid operationId, Guid reservationId);
}
