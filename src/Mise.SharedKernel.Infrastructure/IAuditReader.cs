namespace Mise.SharedKernel.Infrastructure;

/// <summary>
/// US-05 AC #2 ("open a reservation's history, see a chronological list of every change") —
/// the read side of the audit trail, Phase 9. Unlike <see cref="IAuditWriter"/>, which is
/// registered per module so a write lands on the same <c>DbContext</c>/transaction as the
/// mutation it audits, a read has no such constraint: every module's <c>DbContext</c> maps the
/// identical physical <c>shared.audit_log_entry</c> table, so one implementation, registered
/// once at the composition root, serves every entity type. See CLAUDE.md's "Audit completeness"
/// section (ADR-009) for the full reasoning.
/// </summary>
public interface IAuditReader
{
    /// <summary>Newest first.</summary>
    Task<IReadOnlyList<AuditLogEntry>> GetHistoryAsync(
        string entityType, Guid entityId, CancellationToken cancellationToken);
}
