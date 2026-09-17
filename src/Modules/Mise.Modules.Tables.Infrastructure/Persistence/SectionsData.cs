using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Mise.Modules.Tables.Application.Ports;
using Mise.Modules.Tables.Domain;
using Mise.SharedKernel.Persistence;

namespace Mise.Modules.Tables.Infrastructure.Persistence;

internal sealed partial class SectionsData(TablesDbContext dbContext, TimeProvider timeProvider, ILogger<SectionsData> logger)
    : ISectionsData
{
    public async Task<SectionMutationResult> CreateSectionAsync(Section section, Guid operationId, CancellationToken cancellationToken)
    {
        var existingResourceId = await FindProcessedResourceIdAsync(operationId, cancellationToken);
        if (existingResourceId is { } resourceId)
        {
            LogIdempotencyCheckHit(operationId, resourceId);
            var existing = await dbContext.Sections.AsNoTracking().SingleAsync(s => s.Id == resourceId, cancellationToken);
            return new SectionMutationResult(existing, WasAlreadyProcessed: true);
        }

        dbContext.Sections.Add(section);
        RecordProcessedOperation(operationId, section.Id);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new SectionMutationResult(section, WasAlreadyProcessed: false);
    }

    public Task<Section?> GetSectionByIdAsync(Guid sectionId, CancellationToken cancellationToken) =>
        dbContext.Sections.SingleOrDefaultAsync(s => s.Id == sectionId, cancellationToken);

    public async Task<IReadOnlyList<Section>> GetActiveSectionsAsync(CancellationToken cancellationToken) =>
        await dbContext.Sections.AsNoTracking()
            .Where(s => s.IsActive)
            .OrderBy(s => s.DisplayOrder)
            .ToListAsync(cancellationToken);

    public async Task<SectionMutationResult> UpdateSectionAsync(Section section, Guid operationId, CancellationToken cancellationToken)
    {
        var existingResourceId = await FindProcessedResourceIdAsync(operationId, cancellationToken);
        if (existingResourceId is not null)
        {
            LogIdempotencyCheckHit(operationId, section.Id);
            return new SectionMutationResult(section, WasAlreadyProcessed: true);
        }

        RecordProcessedOperation(operationId, section.Id);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new SectionMutationResult(section, WasAlreadyProcessed: false);
    }

    public Task<SectionMutationResult> DeactivateSectionAsync(Section section, Guid operationId, CancellationToken cancellationToken) =>
        UpdateSectionAsync(section, operationId, cancellationToken);

    private async Task<Guid?> FindProcessedResourceIdAsync(Guid operationId, CancellationToken cancellationToken) =>
        await dbContext.ProcessedOperations.AsNoTracking()
            .Where(p => p.OperationId == operationId)
            .Select(p => (Guid?)p.ResourceId)
            .SingleOrDefaultAsync(cancellationToken);

    private void RecordProcessedOperation(Guid operationId, Guid resourceId) =>
        dbContext.ProcessedOperations.Add(new ProcessedOperation
        {
            OperationId = operationId,
            ResourceType = "Section",
            ResourceId = resourceId,
            ProcessedAtUtc = timeProvider.GetUtcNow(),
        });

    [LoggerMessage(EventId = 60, Level = LogLevel.Debug,
        Message = "OperationId {OperationId} already recorded in shared.processed_operation, pointing at resource {ResourceId}.")]
    private partial void LogIdempotencyCheckHit(Guid operationId, Guid resourceId);
}
