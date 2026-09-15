using Microsoft.Extensions.Logging;
using Mise.SharedKernel.Infrastructure;

namespace Mise.Modules.Reservations.Infrastructure.Persistence;

/// <summary>
/// A separate SaveChangesAsync call from the mutation it audits — a known, accepted gap
/// until Phase 9's SaveChangesInterceptor makes the write atomic with the mutation across
/// every module.
/// </summary>
internal sealed partial class AuditWriter(ReservationsDbContext dbContext, ILogger<AuditWriter> logger) : IAuditWriter
{
    public async Task WriteAsync(AuditLogEntry entry, CancellationToken cancellationToken)
    {
        dbContext.AuditLogEntries.Add(entry);
        await dbContext.SaveChangesAsync(cancellationToken);
        LogAuditEntryWritten(entry.Id, entry.EntityType, entry.EntityId, entry.Action);
    }

    [LoggerMessage(EventId = 20, Level = LogLevel.Debug,
        Message = "Audit entry {AuditEntryId} written for {EntityType} {EntityId}, action {Action}.")]
    private partial void LogAuditEntryWritten(Guid auditEntryId, string entityType, Guid entityId, string action);
}
