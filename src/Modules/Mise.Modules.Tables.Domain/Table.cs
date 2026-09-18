using Mise.SharedKernel;

namespace Mise.Modules.Tables.Domain;

/// <summary>
/// <see cref="Deactivate"/>'s guard against an Occupied/Reserved table is docs/plan.md
/// correction #13's stand-in for US-04's "blocked while it has an active reservation" edge
/// case: Reservations doesn't reference Table at all until Phase 6/7, so nothing outside this
/// aggregate can answer that question yet. <see cref="Status"/> is this aggregate's own field,
/// so the guard is a genuine, self-contained Domain invariant today — not a placeholder.
/// Phase 7 starts actually driving <see cref="Status"/> away from
/// <see cref="TableStatus.Available"/>, via three distinct entry points: <see cref="SetStatus"/>
/// (FR-06, a direct, unconditional staff override — no business-rule gate, matching the
/// charter's "change a table's status directly ... independent of a reservation"),
/// <see cref="MarkOccupied"/> (the cross-module side effect of FR-05's seat action), and
/// <see cref="ReleaseIfReservationHeld"/> (BR-05's cross-module release). The latter two report
/// whether they actually changed anything, so their Application-layer caller (an event handler,
/// not a REST-triggered command) can skip a redundant persist/audit write when a cross-module
/// event is redispatched on a client's OperationId replay and the table already reflects the
/// intended state.
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

    /// <summary>FR-06: "Staff can change a table's status directly during service ... independent
    /// of a reservation." Deliberately unconditional — no transition guard between any two of
    /// the five statuses — because this is a floor-staff override action, not a business-rule
    /// gated state machine; the charter's own phrasing ("independent of a reservation") reads as
    /// "no gate" rather than a restricted transition table.</summary>
    public void SetStatus(TableStatus newStatus) => Status = newStatus;

    /// <summary>The cross-module side effect of FR-05's seat action (BR-04's "cannot be marked
    /// Seated without an assigned table" is enforced on the Reservation side —
    /// <see cref="Mise.SharedKernel.IDomainEvent"/> propagation here just reflects that decision
    /// onto the table). Unconditional set, but reports whether it actually changed
    /// <see cref="Status"/> (false when already Occupied) so a redispatched event on a
    /// reservation-side OperationId replay doesn't force a redundant persist/audit write.</summary>
    public bool MarkOccupied()
    {
        if (Status == TableStatus.Occupied)
        {
            return false;
        }

        Status = TableStatus.Occupied;
        return true;
    }

    /// <summary>BR-05's "frees its table" — but only when the current status was itself
    /// reservation-driven (Reserved or Occupied); a staff-driven <see cref="SetStatus"/> override
    /// (NeedsCleaning, Blocked) is left untouched, so an automatic release can never silently
    /// clobber an explicit FR-06 action. The "unless another active reservation holds it" half of
    /// BR-05 is decided by the caller (Tables.Application's event handler, via the cross-module
    /// <c>IReservationLookup</c> — CLAUDE.md's ADR-007) *before* this method is even called; this
    /// method only knows this aggregate's own field, same self-contained-invariant category as
    /// <see cref="Deactivate"/>'s guard.</summary>
    public bool ReleaseIfReservationHeld()
    {
        if (Status is not (TableStatus.Reserved or TableStatus.Occupied))
        {
            return false;
        }

        Status = TableStatus.Available;
        return true;
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
