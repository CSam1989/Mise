using Microsoft.EntityFrameworkCore;
using Mise.SharedKernel.Infrastructure;

namespace Mise.SharedKernel.Persistence;

/// <summary>
/// Backed by whichever <typeparamref name="TDbContext"/> it's registered against — every
/// module's DbContext maps the same physical <c>shared.audit_log_entry</c> table, so any one of
/// them reads the full cross-module history. Registered once, at the composition root, against
/// <c>ReservationsDbContext</c> (the table's migration owner) — see
/// <see cref="IAuditReader"/>'s own doc comment.
/// </summary>
public sealed class AuditReader<TDbContext>(TDbContext dbContext) : IAuditReader
    where TDbContext : DbContext
{
    public async Task<IReadOnlyList<AuditLogEntry>> GetHistoryAsync(
        string entityType, Guid entityId, CancellationToken cancellationToken) =>
        await dbContext.Set<AuditLogEntry>()
            .AsNoTracking()
            .Where(e => e.EntityType == entityType && e.EntityId == entityId)
            .OrderByDescending(e => e.OccurredAtUtc)
            .ToListAsync(cancellationToken);
}
