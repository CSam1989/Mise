using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Mise.SharedKernel.Infrastructure;

namespace Mise.SharedKernel.Persistence;

/// <summary>
/// Generic over the calling module's own DbContext so each module keeps its own
/// audit_log_entry rows in its own connection/transaction — there is deliberately no shared
/// DbContext (CLAUDE.md's cross-cutting infra note). <see cref="Stage"/> only adds to the
/// change tracker; it is the caller's own imminent <c>SaveChangesAsync</c> (the gateway
/// mutation call staging happens in front of) that actually persists it, together with the
/// mutation, in one atomic call — <c>AuditCompletenessInterceptor</c> is the runtime guarantee
/// that a handler staging path was actually exercised (Phase 9, ADR-009).
/// </summary>
public sealed partial class AuditWriter<TDbContext>(TDbContext dbContext, ILogger<AuditWriter<TDbContext>> logger)
    : IAuditWriter
    where TDbContext : DbContext
{
    public void Stage(AuditLogEntry entry)
    {
        dbContext.Set<AuditLogEntry>().Add(entry);
        LogAuditEntryStaged(entry.Id, entry.EntityType, entry.EntityId, entry.Action);
    }

    public async Task WriteAsync(AuditLogEntry entry, CancellationToken cancellationToken)
    {
        dbContext.Set<AuditLogEntry>().Add(entry);
        await dbContext.SaveChangesAsync(cancellationToken);
        LogAuditEntryWritten(entry.Id, entry.EntityType, entry.EntityId, entry.Action);
    }

    [LoggerMessage(EventId = 20, Level = LogLevel.Debug,
        Message = "Audit entry {AuditEntryId} written for {EntityType} {EntityId}, action {Action}.")]
    private partial void LogAuditEntryWritten(Guid auditEntryId, string entityType, Guid entityId, string action);

    [LoggerMessage(EventId = 21, Level = LogLevel.Debug,
        Message = "Audit entry {AuditEntryId} staged for {EntityType} {EntityId}, action {Action}.")]
    private partial void LogAuditEntryStaged(Guid auditEntryId, string entityType, Guid entityId, string action);
}
