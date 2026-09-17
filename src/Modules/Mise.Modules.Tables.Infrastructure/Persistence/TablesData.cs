using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Mise.Modules.Tables.Application.Ports;
using Mise.Modules.Tables.Domain;
using Mise.SharedKernel.Persistence;

namespace Mise.Modules.Tables.Infrastructure.Persistence;

/// <summary>
/// The "Version" shadow property (set up by TableConfiguration's <c>IsRowVersion()</c>, which
/// Npgsql's own convention silently maps to the physical <c>xmin</c> system column — see that
/// configuration's doc comment) is read/written here exclusively — Application never touches
/// EF Core directly (CLAUDE.md's testable seam), so it only ever sees the version as an opaque
/// uint round-tripped through <see cref="Application.Ports.TableWithVersion"/> /
/// <see cref="Application.Ports.TableSaveResult"/>.
/// </summary>
internal sealed partial class TablesData(TablesDbContext dbContext, TimeProvider timeProvider, ILogger<TablesData> logger)
    : ITablesData
{
    public async Task<TableCreateResult> CreateTableAsync(Table table, Guid operationId, CancellationToken cancellationToken)
    {
        var existingResourceId = await FindProcessedResourceIdAsync(operationId, cancellationToken);
        if (existingResourceId is { } resourceId)
        {
            LogIdempotencyCheckHit(operationId, resourceId);
            var existingVersion = await dbContext.Tables.AsNoTracking()
                .Where(t => t.Id == resourceId)
                .Select(t => EF.Property<uint>(t, "Version"))
                .SingleAsync(cancellationToken);
            return new TableCreateResult(resourceId, existingVersion, WasAlreadyProcessed: true);
        }

        dbContext.Tables.Add(table);
        RecordProcessedOperation(operationId, table.Id);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new TableCreateResult(table.Id, CurrentVersionOf(table), WasAlreadyProcessed: false);
    }

    public async Task<TableWithVersion?> GetTableByIdAsync(Guid tableId, CancellationToken cancellationToken)
    {
        var table = await dbContext.Tables.SingleOrDefaultAsync(t => t.Id == tableId, cancellationToken);
        if (table is null)
        {
            return null;
        }

        return new TableWithVersion(table, CurrentVersionOf(table));
    }

    public async Task<IReadOnlyList<TableWithVersion>> GetFloorPlanAsync(Guid? sectionId, CancellationToken cancellationToken)
    {
        var query = dbContext.Tables.AsNoTracking().Where(t => t.IsActive);
        if (sectionId is { } id)
        {
            query = query.Where(t => t.SectionId == id);
        }

        var rows = await query
            .Select(t => new { Table = t, Version = EF.Property<uint>(t, "Version") })
            .ToListAsync(cancellationToken);

        return rows.Select(r => new TableWithVersion(r.Table, r.Version)).ToList();
    }

    public Task<TableSaveResult> UpdateTableAsync(Table table, uint expectedVersion, Guid operationId, CancellationToken cancellationToken) =>
        SaveWithConcurrencyCheckAsync(table, expectedVersion, operationId, cancellationToken);

    public Task<TableSaveResult> DeactivateTableAsync(Table table, uint expectedVersion, Guid operationId, CancellationToken cancellationToken) =>
        SaveWithConcurrencyCheckAsync(table, expectedVersion, operationId, cancellationToken);

    public Task<bool> AnyActiveTablesInSectionAsync(Guid sectionId, CancellationToken cancellationToken) =>
        dbContext.Tables.AsNoTracking().AnyAsync(t => t.SectionId == sectionId && t.IsActive, cancellationToken);

    private async Task<TableSaveResult> SaveWithConcurrencyCheckAsync(
        Table table, uint expectedVersion, Guid operationId, CancellationToken cancellationToken)
    {
        var existingResourceId = await FindProcessedResourceIdAsync(operationId, cancellationToken);
        if (existingResourceId is not null)
        {
            LogIdempotencyCheckHit(operationId, table.Id);
            return new TableSaveResult(TableSaveOutcome.Saved, table, CurrentVersionOf(table), WasAlreadyProcessed: true);
        }

        dbContext.Entry(table).Property<uint>("Version").OriginalValue = expectedVersion;
        RecordProcessedOperation(operationId, table.Id);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            LogVersionMismatch(table.Id, expectedVersion);
            var current = await dbContext.Tables.AsNoTracking()
                .Where(t => t.Id == table.Id)
                .Select(t => new { Table = t, Version = EF.Property<uint>(t, "Version") })
                .SingleAsync(cancellationToken);
            return new TableSaveResult(TableSaveOutcome.VersionMismatch, current.Table, current.Version, WasAlreadyProcessed: false);
        }

        return new TableSaveResult(TableSaveOutcome.Saved, table, CurrentVersionOf(table), WasAlreadyProcessed: false);
    }

    private uint CurrentVersionOf(Table table) => dbContext.Entry(table).Property<uint>("Version").CurrentValue;

    private async Task<Guid?> FindProcessedResourceIdAsync(Guid operationId, CancellationToken cancellationToken) =>
        await dbContext.ProcessedOperations.AsNoTracking()
            .Where(p => p.OperationId == operationId)
            .Select(p => (Guid?)p.ResourceId)
            .SingleOrDefaultAsync(cancellationToken);

    private void RecordProcessedOperation(Guid operationId, Guid resourceId) =>
        dbContext.ProcessedOperations.Add(new ProcessedOperation
        {
            OperationId = operationId,
            ResourceType = "Table",
            ResourceId = resourceId,
            ProcessedAtUtc = timeProvider.GetUtcNow(),
        });

    [LoggerMessage(EventId = 61, Level = LogLevel.Debug,
        Message = "OperationId {OperationId} already recorded in shared.processed_operation, pointing at resource {ResourceId}.")]
    private partial void LogIdempotencyCheckHit(Guid operationId, Guid resourceId);

    [LoggerMessage(EventId = 62, Level = LogLevel.Debug,
        Message = "Table {TableId} save rejected by Postgres: caller's xmin {ExpectedVersion} no longer matches the current row.")]
    private partial void LogVersionMismatch(Guid tableId, uint expectedVersion);
}
