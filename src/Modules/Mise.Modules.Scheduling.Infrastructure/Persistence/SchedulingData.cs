using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Mise.Modules.Scheduling.Application.Ports;
using Mise.Modules.Scheduling.Domain;
using Mise.SharedKernel.Persistence;

namespace Mise.Modules.Scheduling.Infrastructure.Persistence;

internal sealed partial class SchedulingData(SchedulingDbContext dbContext, TimeProvider timeProvider, ILogger<SchedulingData> logger)
    : ISchedulingData
{
    public async Task<ServicePeriodMutationResult> CreateServicePeriodAsync(
        ServicePeriod servicePeriod, Guid operationId, CancellationToken cancellationToken)
    {
        var existingResourceId = await FindProcessedResourceIdAsync(operationId, cancellationToken);
        if (existingResourceId is { } resourceId)
        {
            LogIdempotencyCheckHit(operationId, resourceId);
            var existing = await dbContext.ServicePeriods.AsNoTracking().SingleAsync(s => s.Id == resourceId, cancellationToken);
            return new ServicePeriodMutationResult(existing, WasAlreadyProcessed: true);
        }

        dbContext.ServicePeriods.Add(servicePeriod);
        RecordProcessedOperation(operationId, servicePeriod.Id);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new ServicePeriodMutationResult(servicePeriod, WasAlreadyProcessed: false);
    }

    public Task<ServicePeriod?> GetServicePeriodByIdAsync(Guid servicePeriodId, CancellationToken cancellationToken) =>
        dbContext.ServicePeriods.SingleOrDefaultAsync(s => s.Id == servicePeriodId, cancellationToken);

    public async Task<IReadOnlyList<ServicePeriod>> GetServicePeriodsForDateAsync(DateOnly date, CancellationToken cancellationToken) =>
        await dbContext.ServicePeriods.AsNoTracking()
            .Where(s => s.Date == date)
            .OrderBy(s => s.StartTime)
            .ToListAsync(cancellationToken);

    public async Task<ServicePeriodMutationResult> UpdateServicePeriodAsync(
        ServicePeriod servicePeriod, Guid operationId, CancellationToken cancellationToken)
    {
        var existingResourceId = await FindProcessedResourceIdAsync(operationId, cancellationToken);
        if (existingResourceId is not null)
        {
            LogIdempotencyCheckHit(operationId, servicePeriod.Id);
            return new ServicePeriodMutationResult(servicePeriod, WasAlreadyProcessed: true);
        }

        RecordProcessedOperation(operationId, servicePeriod.Id);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new ServicePeriodMutationResult(servicePeriod, WasAlreadyProcessed: false);
    }

    public async Task<ServicePeriodDeletionResult> DeleteServicePeriodAsync(
        Guid servicePeriodId, Guid operationId, CancellationToken cancellationToken)
    {
        // OperationId checked BEFORE the row lookup: a hard delete makes the row disappear
        // after the first successful call, so a replay must recognise itself from the
        // OperationId table alone, not from the row still being there (ISchedulingData's remarks).
        var existingResourceId = await FindProcessedResourceIdAsync(operationId, cancellationToken);
        if (existingResourceId is not null)
        {
            LogIdempotencyCheckHit(operationId, existingResourceId.Value);
            return new ServicePeriodDeletionResult(Existed: true, WasAlreadyProcessed: true);
        }

        var servicePeriod = await dbContext.ServicePeriods.SingleOrDefaultAsync(s => s.Id == servicePeriodId, cancellationToken);
        if (servicePeriod is null)
        {
            return new ServicePeriodDeletionResult(Existed: false, WasAlreadyProcessed: false);
        }

        dbContext.ServicePeriods.Remove(servicePeriod);
        RecordProcessedOperation(operationId, servicePeriodId);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new ServicePeriodDeletionResult(Existed: true, WasAlreadyProcessed: false);
    }

    private async Task<Guid?> FindProcessedResourceIdAsync(Guid operationId, CancellationToken cancellationToken) =>
        await dbContext.ProcessedOperations.AsNoTracking()
            .Where(p => p.OperationId == operationId)
            .Select(p => (Guid?)p.ResourceId)
            .SingleOrDefaultAsync(cancellationToken);

    private void RecordProcessedOperation(Guid operationId, Guid resourceId) =>
        dbContext.ProcessedOperations.Add(new ProcessedOperation
        {
            OperationId = operationId,
            ResourceType = "ServicePeriod",
            ResourceId = resourceId,
            ProcessedAtUtc = timeProvider.GetUtcNow(),
        });

    [LoggerMessage(EventId = 90, Level = LogLevel.Debug,
        Message = "OperationId {OperationId} already recorded in shared.processed_operation, pointing at resource {ResourceId}.")]
    private partial void LogIdempotencyCheckHit(Guid operationId, Guid resourceId);
}
