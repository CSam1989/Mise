namespace Mise.Modules.Tables.Contracts;

/// <summary>
/// The first real cross-module read in this codebase (CLAUDE.md's Reservations Phase 6
/// section / ADR-006) — Mise.Modules.Reservations.Application depends on this interface
/// directly (Application may reference another module's Contracts, never its
/// Application/Infrastructure/Domain — ModuleBoundaryTests), to answer BR-07 ("party size must
/// fit within the assigned table's capacity, or an explicitly combinable set of tables")
/// without Reservations ever knowing a <c>Table</c> or <c>TableGroup</c> entity exists.
/// Implemented by Mise.Modules.Tables.Infrastructure, registered in DI at the Mise.ApiService
/// composition root.
/// </summary>
public interface ITableAvailabilityLookup
{
    Task<TableCapacityInfo?> GetCapacityInfoAsync(Guid tableId, CancellationToken cancellationToken);
}

/// <param name="CombinedMinCapacity">The table's own MinCapacity, or — if it belongs to an
/// active TableGroup — the sum of every member table's MinCapacity in that group.</param>
/// <param name="CombinedMaxCapacity">Same, for MaxCapacity.</param>
public sealed record TableCapacityInfo(Guid TableId, bool IsActive, int CombinedMinCapacity, int CombinedMaxCapacity);
