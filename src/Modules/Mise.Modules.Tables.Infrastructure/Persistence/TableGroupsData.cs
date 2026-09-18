using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Mise.Modules.Tables.Application.Ports;
using Mise.Modules.Tables.Domain;
using Mise.SharedKernel.Persistence;

namespace Mise.Modules.Tables.Infrastructure.Persistence;

internal sealed partial class TableGroupsData(TablesDbContext dbContext, TimeProvider timeProvider, ILogger<TableGroupsData> logger)
    : ITableGroupsData
{
    public async Task<Guid?> FindExistingTableGroupIdAsync(Guid operationId, CancellationToken cancellationToken) =>
        await dbContext.ProcessedOperations.AsNoTracking()
            .Where(p => p.OperationId == operationId)
            .Select(p => (Guid?)p.ResourceId)
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<TableGroupCreateResult> CreateTableGroupAsync(
        TableGroup tableGroup, Guid operationId, CancellationToken cancellationToken)
    {
        var existingResourceId = await FindExistingTableGroupIdAsync(operationId, cancellationToken);

        if (existingResourceId is { } resourceId)
        {
            LogIdempotencyCheckHit(operationId, resourceId);
            return new TableGroupCreateResult(resourceId, WasAlreadyProcessed: true);
        }

        dbContext.TableGroups.Add(tableGroup);
        dbContext.ProcessedOperations.Add(new ProcessedOperation
        {
            OperationId = operationId,
            ResourceType = "TableGroup",
            ResourceId = tableGroup.Id,
            ProcessedAtUtc = timeProvider.GetUtcNow(),
        });

        await dbContext.SaveChangesAsync(cancellationToken);

        return new TableGroupCreateResult(tableGroup.Id, WasAlreadyProcessed: false);
    }

    public async Task<IReadOnlyList<Guid>> GetTableIdsAlreadyInAnActiveGroupAsync(
        IReadOnlyList<Guid> candidateTableIds, CancellationToken cancellationToken)
    {
        // Small, restaurant-scale row count (see TableGroupConfiguration's doc comment) — read
        // every active group's member array back into memory rather than querying the uuid[]
        // column with a Postgres array operator, which would need raw SQL to express cleanly.
        var activeGroupMemberIds = await dbContext.TableGroups.AsNoTracking()
            .Where(g => g.IsActive)
            .Select(g => g.TableIds)
            .ToListAsync(cancellationToken);

        var alreadyGrouped = activeGroupMemberIds.SelectMany(ids => ids).ToHashSet();
        return candidateTableIds.Where(alreadyGrouped.Contains).ToArray();
    }

    [LoggerMessage(EventId = 72, Level = LogLevel.Debug,
        Message = "OperationId {OperationId} already recorded in shared.processed_operation, pointing at resource {ResourceId}.")]
    private partial void LogIdempotencyCheckHit(Guid operationId, Guid resourceId);
}
