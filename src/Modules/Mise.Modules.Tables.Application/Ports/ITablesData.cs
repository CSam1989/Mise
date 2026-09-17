using Mise.Modules.Tables.Domain;

namespace Mise.Modules.Tables.Application.Ports;

/// <summary>
/// Slice-specific gateway (CLAUDE.md "The testable seam"). <see cref="TableWithVersion"/>'s
/// Version is the Postgres <c>xmin</c> value the Infrastructure implementation reads via
/// Npgsql's <c>UseXminAsConcurrencyToken()</c> (docs/plan.md correction #5) — Application
/// never touches EF Core directly, so it only ever sees this as an opaque <see cref="uint"/>
/// round-tripped from the caller's <c>If-Match</c> header.
/// </summary>
public interface ITablesData
{
    Task<TableCreateResult> CreateTableAsync(Table table, Guid operationId, CancellationToken cancellationToken);

    Task<TableWithVersion?> GetTableByIdAsync(Guid tableId, CancellationToken cancellationToken);

    Task<IReadOnlyList<TableWithVersion>> GetFloorPlanAsync(Guid? sectionId, CancellationToken cancellationToken);

    /// <param name="table">Already loaded (via <see cref="GetTableByIdAsync"/>) and mutated by
    /// the caller via a Domain method — this call persists it under an optimistic-concurrency
    /// check against <paramref name="expectedVersion"/>.</param>
    Task<TableSaveResult> UpdateTableAsync(Table table, uint expectedVersion, Guid operationId, CancellationToken cancellationToken);

    Task<TableSaveResult> DeactivateTableAsync(Table table, uint expectedVersion, Guid operationId, CancellationToken cancellationToken);

    /// <summary>Backs DeactivateSectionCommandHandler's guard — a cross-aggregate,
    /// database-dependent check Section itself has no way to answer (see Section's doc
    /// comment).</summary>
    Task<bool> AnyActiveTablesInSectionAsync(Guid sectionId, CancellationToken cancellationToken);
}

public sealed record TableCreateResult(Guid TableId, uint Version, bool WasAlreadyProcessed);

public sealed record TableWithVersion(Table Table, uint Version);

public enum TableSaveOutcome
{
    Saved,
    VersionMismatch,
}

public sealed record TableSaveResult(TableSaveOutcome Outcome, Table Table, uint Version, bool WasAlreadyProcessed);
