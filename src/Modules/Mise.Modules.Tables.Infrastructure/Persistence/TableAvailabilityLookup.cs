using Microsoft.EntityFrameworkCore;
using Mise.Modules.Tables.Contracts;

namespace Mise.Modules.Tables.Infrastructure.Persistence;

/// <summary>
/// Implements the Tables module's own Contracts interface (CLAUDE.md's Reservations Phase 6
/// section / ADR-006) — allowed by ModuleBoundaryTests.Infrastructure_IsReferencedByNothingExceptTheCompositionRoot,
/// which forbids another module's Domain/Application/Contracts from referencing THIS module's
/// Infrastructure, not the reverse. Registered in DI at Mise.ApiService's composition root and
/// consumed by Mise.Modules.Reservations.Application, which references
/// Mise.Modules.Tables.Contracts directly (an allowed cross-module reference) but never this
/// class or this project.
/// </summary>
internal sealed class TableAvailabilityLookup(TablesDbContext dbContext) : ITableAvailabilityLookup
{
    public async Task<TableCapacityInfo?> GetCapacityInfoAsync(Guid tableId, CancellationToken cancellationToken)
    {
        var table = await dbContext.Tables.AsNoTracking()
            .SingleOrDefaultAsync(t => t.Id == tableId, cancellationToken);
        if (table is null)
        {
            return null;
        }

        var group = await dbContext.TableGroups.AsNoTracking()
            .Where(g => g.IsActive && g.TableIds.Contains(tableId))
            .SingleOrDefaultAsync(cancellationToken);

        if (group is null)
        {
            return new TableCapacityInfo(tableId, table.IsActive, table.MinCapacity, table.MaxCapacity);
        }

        var members = await dbContext.Tables.AsNoTracking()
            .Where(t => group.TableIds.Contains(t.Id))
            .Select(t => new { t.MinCapacity, t.MaxCapacity })
            .ToListAsync(cancellationToken);

        return new TableCapacityInfo(
            tableId, table.IsActive, members.Sum(m => m.MinCapacity), members.Sum(m => m.MaxCapacity));
    }
}
