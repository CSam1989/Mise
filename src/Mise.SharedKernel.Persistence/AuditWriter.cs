using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Mise.SharedKernel.Infrastructure;

namespace Mise.SharedKernel.Persistence;

/// <summary>
/// Generic over the calling module's own DbContext so each module keeps its own
/// audit_log_entry rows in its own connection/transaction — there is deliberately no shared
/// DbContext (CLAUDE.md's cross-cutting infra note). A separate SaveChangesAsync call from
/// the mutation it audits, same known gap as Phase 2 shipped: Phase 9's SaveChangesInterceptor
/// makes the write atomic with the mutation across every module.
/// </summary>
public sealed partial class AuditWriter<TDbContext>(TDbContext dbContext, ILogger<AuditWriter<TDbContext>> logger)
    : IAuditWriter
    where TDbContext : DbContext
{
    public async Task WriteAsync(AuditLogEntry entry, CancellationToken cancellationToken)
    {
        dbContext.Set<AuditLogEntry>().Add(entry);
        await dbContext.SaveChangesAsync(cancellationToken);
        LogAuditEntryWritten(entry.Id, entry.EntityType, entry.EntityId, entry.Action);
    }

    [LoggerMessage(EventId = 20, Level = LogLevel.Debug,
        Message = "Audit entry {AuditEntryId} written for {EntityType} {EntityId}, action {Action}.")]
    private partial void LogAuditEntryWritten(Guid auditEntryId, string entityType, Guid entityId, string action);
}
