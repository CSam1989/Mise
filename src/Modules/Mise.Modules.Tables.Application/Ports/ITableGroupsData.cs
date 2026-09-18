using Mise.Modules.Tables.Domain;

namespace Mise.Modules.Tables.Application.Ports;

/// <summary>
/// Slice-specific gateway (CLAUDE.md "The testable seam"), same shape as
/// <see cref="ITablesData"/>/<see cref="ISectionsData"/>. No xmin/ETag here — Create-only this
/// phase (no Update/Deactivate exists yet), same scope decision Section made before Table's
/// concurrency needs forced it there.
/// </summary>
public interface ITableGroupsData
{
    Task<TableGroupCreateResult> CreateTableGroupAsync(TableGroup tableGroup, Guid operationId, CancellationToken cancellationToken);

    /// <summary>Backs CreateTableGroupCommandHandler's own idempotency short-circuit, checked
    /// *before* the "not already in another active group" check below — a replay of an
    /// already-processed OperationId would otherwise fail that check for exactly the tables its
    /// own first run just grouped (the same "replay's idempotency check must run before the
    /// existence check" pitfall CLAUDE.md's Scheduling section already documents, caught here by
    /// the same kind of failing test, not by inspection). <see cref="CreateTableGroupAsync"/>
    /// still re-checks internally too — this is purely so the handler's cross-aggregate
    /// validation never runs at all on a replay.</summary>
    Task<Guid?> FindExistingTableGroupIdAsync(Guid operationId, CancellationToken cancellationToken);

    /// <summary>Backs CreateTableGroupCommandHandler's "not already in another active group"
    /// check — a cross-aggregate query no single <see cref="TableGroup"/> instance could answer
    /// about itself.</summary>
    Task<IReadOnlyList<Guid>> GetTableIdsAlreadyInAnActiveGroupAsync(
        IReadOnlyList<Guid> candidateTableIds, CancellationToken cancellationToken);
}

public sealed record TableGroupCreateResult(Guid TableGroupId, bool WasAlreadyProcessed);
