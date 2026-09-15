namespace Mise.Modules.Reservations.Infrastructure.Persistence;

/// <summary>
/// One row in shared.processed_operation — module-internal today (only Reservations has a
/// DbContext). A second module needing OperationId idempotency decides then whether to
/// extract this into a shared project or map the same table from its own DbContext; not
/// solved speculatively here.
/// </summary>
internal sealed class ProcessedOperation
{
    public Guid OperationId { get; set; }
    public string ResourceType { get; set; } = string.Empty;
    public Guid ResourceId { get; set; }
    public DateTimeOffset ProcessedAtUtc { get; set; }
}
