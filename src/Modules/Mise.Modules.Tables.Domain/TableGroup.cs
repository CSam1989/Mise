using Mise.SharedKernel;

namespace Mise.Modules.Tables.Domain;

/// <summary>
/// Resolves docs/plan.md's "combinable set of tables" open question (blocked Phase 6 until
/// decided — see CLAUDE.md's Reservations Phase 6 section / ADR-006): an explicit aggregate
/// rather than a self-referencing pointer on <see cref="Table"/>, because a group is a concept
/// with its own lifecycle (a Manager creates it, names it) independent of any one member table.
/// Membership eligibility (all members must have <see cref="Table.IsCombinable"/> true, must be
/// active, must not already belong to another active group) is a cross-aggregate, DB-dependent
/// check — same category distinction this module already draws elsewhere — so it lives in
/// Application (<c>CreateTableGroupCommandHandler</c>), not here. This aggregate only enforces
/// the structural, self-contained shape: a name, and at least two distinct member ids (a
/// "group" of one table is not a group).
/// </summary>
public sealed class TableGroup : AggregateRoot<Guid>
{
    private readonly List<Guid> _tableIds;

    private TableGroup(Guid id, string name, IReadOnlyList<Guid> tableIds, bool isActive)
        : base(id)
    {
        Name = name;
        _tableIds = [.. tableIds];
        IsActive = isActive;
    }

    public string Name { get; private set; }
    public IReadOnlyList<Guid> TableIds => _tableIds;
    public bool IsActive { get; private set; }

    public static TableGroup Create(Guid id, string name, IReadOnlyList<Guid> tableIds)
    {
        Guard.Against.NullOrWhiteSpace(name, nameof(name));

        var distinctIds = tableIds.Distinct().ToArray();
        if (distinctIds.Length < 2)
        {
            throw new ArgumentException("A table group must have at least two distinct tables.", nameof(tableIds));
        }

        return new TableGroup(id, name, distinctIds, isActive: true);
    }
}
