namespace Mise.SharedKernel.Persistence;

/// <summary>
/// One row in shared.processed_operation. Extracted from Reservations.Infrastructure in
/// Phase 3 — StaffIdentity is the second module needing OperationId idempotency, which is
/// what CLAUDE.md's cross-cutting infra note names as the trigger to stop duplicating this
/// per module. Public (not internal) because more than one module's Infrastructure assembly
/// now references it.
/// </summary>
public sealed class ProcessedOperation
{
    public Guid OperationId { get; set; }
    public string ResourceType { get; set; } = string.Empty;
    public Guid ResourceId { get; set; }
    public DateTimeOffset ProcessedAtUtc { get; set; }
}
