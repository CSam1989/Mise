using Mise.SharedKernel;

namespace Mise.Modules.Tables.Domain;

/// <summary>
/// <see cref="IsActive"/> is docs/plan.md correction #12: the charter's original data model
/// (§10) gave FR-07's "deactivate ... sections" no field to act on — only <see cref="Table"/>
/// had one. Whether a section being deactivated still has active tables is a cross-aggregate,
/// database-dependent question this type can't answer about itself, so that guard lives in
/// DeactivateSectionCommandHandler (mirrors RegisterStaffCommandHandler's taken-username
/// check — a field-scoped ValidationException from the handler, not a Domain invariant here).
/// </summary>
public sealed class Section : AggregateRoot<Guid>, IAuditableEntity
{
    private Section(Guid id, string name, int displayOrder, bool isActive) : base(id)
    {
        Name = name;
        DisplayOrder = displayOrder;
        IsActive = isActive;
    }

    public string Name { get; private set; }
    public int DisplayOrder { get; private set; }
    public bool IsActive { get; private set; }

    public static Section Create(Guid id, string name, int displayOrder)
    {
        ValidateFields(name, displayOrder);
        return new Section(id, name, displayOrder, isActive: true);
    }

    public void UpdateDetails(string name, int displayOrder)
    {
        ValidateFields(name, displayOrder);
        Name = name;
        DisplayOrder = displayOrder;
    }

    /// <summary>Unconditional — the "still has active tables" guard needs a database query
    /// this aggregate has no way to run, so it lives in the Application handler instead.</summary>
    public void Deactivate() => IsActive = false;

    private static void ValidateFields(string name, int displayOrder)
    {
        Guard.Against.NullOrWhiteSpace(name, nameof(name));
        if (displayOrder < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(displayOrder), displayOrder, "DisplayOrder must not be negative.");
        }
    }
}
