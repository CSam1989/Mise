namespace Mise.SharedKernel.Infrastructure;

/// <summary>
/// One row in shared.audit_log_entry. Exactly one of <see cref="PerformedByStaffId"/> /
/// <see cref="PerformedBySystemProcess"/> is set — StaffIdentity (Phase 3) starts populating
/// the former; until then every writer attributes to a named system process instead.
/// </summary>
public sealed class AuditLogEntry
{
    public Guid Id { get; set; }
    public string EntityType { get; set; } = string.Empty;
    public Guid EntityId { get; set; }
    public string Action { get; set; } = string.Empty;
    public Guid? PerformedByStaffId { get; set; }
    public string? PerformedBySystemProcess { get; set; }
    public DateTimeOffset OccurredAtUtc { get; set; }
    public string Details { get; set; } = string.Empty;
}
