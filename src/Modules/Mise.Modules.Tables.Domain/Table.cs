using Mise.SharedKernel;

namespace Mise.Modules.Tables.Domain;

/// <summary>
/// <see cref="Deactivate"/>'s guard against an Occupied/Reserved table is docs/plan.md
/// correction #13's stand-in for US-04's "blocked while it has an active reservation" edge
/// case: Reservations doesn't reference Table at all until Phase 6/7, so nothing outside this
/// aggregate can answer that question yet. <see cref="Status"/> is this aggregate's own field,
/// so the guard is a genuine, self-contained Domain invariant today — not a placeholder — and
/// needs no rework once Phase 7's cross-module event wiring starts actually driving Status
/// away from <see cref="TableStatus.Available"/>.
/// </summary>
public sealed class Table : AggregateRoot<Guid>
{
    private Table(
        Guid id, Guid sectionId, string name, int minCapacity, int maxCapacity, bool isCombinable,
        double? positionX, double? positionY, TableStatus status, bool isActive)
        : base(id)
    {
        SectionId = sectionId;
        Name = name;
        MinCapacity = minCapacity;
        MaxCapacity = maxCapacity;
        IsCombinable = isCombinable;
        PositionX = positionX;
        PositionY = positionY;
        Status = status;
        IsActive = isActive;
    }

    public Guid SectionId { get; private set; }
    public string Name { get; private set; }
    public int MinCapacity { get; private set; }
    public int MaxCapacity { get; private set; }
    public bool IsCombinable { get; private set; }
    public double? PositionX { get; private set; }
    public double? PositionY { get; private set; }
    public TableStatus Status { get; private set; }
    public bool IsActive { get; private set; }

    public static Table Create(
        Guid id, Guid sectionId, string name, int minCapacity, int maxCapacity, bool isCombinable,
        double? positionX, double? positionY)
    {
        ValidateFields(sectionId, name, minCapacity, maxCapacity);
        return new Table(id, sectionId, name, minCapacity, maxCapacity, isCombinable, positionX, positionY, TableStatus.Available, isActive: true);
    }

    public void UpdateDetails(
        Guid sectionId, string name, int minCapacity, int maxCapacity, bool isCombinable, double? positionX, double? positionY)
    {
        ValidateFields(sectionId, name, minCapacity, maxCapacity);
        SectionId = sectionId;
        Name = name;
        MinCapacity = minCapacity;
        MaxCapacity = maxCapacity;
        IsCombinable = isCombinable;
        PositionX = positionX;
        PositionY = positionY;
    }

    public void Deactivate()
    {
        if (Status is TableStatus.Reserved or TableStatus.Occupied)
        {
            throw new DomainRuleViolationException(
                $"Cannot deactivate table '{Name}' while its status is {Status}.");
        }

        IsActive = false;
    }

    private static void ValidateFields(Guid sectionId, string name, int minCapacity, int maxCapacity)
    {
        Guard.Against.NullOrWhiteSpace(name, nameof(name));
        if (sectionId == Guid.Empty)
        {
            throw new ArgumentException("SectionId must not be empty.", nameof(sectionId));
        }

        Guard.Against.NegativeOrZero(minCapacity, nameof(minCapacity));
        if (maxCapacity < minCapacity)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxCapacity), maxCapacity, "MaxCapacity must be greater than or equal to MinCapacity.");
        }
    }
}
