using Mise.SharedKernel;

namespace Mise.Modules.StaffIdentity.Domain;

/// <summary>
/// The business profile row (charter's StaffUser entity, minus PinHash — that lands with
/// device pairing in Phase 13). 1:1 with an ASP.NET Core Identity user by <see cref="Id"/>,
/// but deliberately a separate row: Identity's own AspNetUsers table owns credentials
/// (username, password hash, security stamp); this aggregate owns the business-meaningful
/// fields Domain actually has invariants over. Never referenced from Infrastructure's
/// Identity plumbing directly — StaffIdentityData is the only thing that joins the two.
/// </summary>
public sealed class StaffUser : AggregateRoot<Guid>, IAuditableEntity
{
    private StaffUser(Guid id, string fullName, StaffRole role, bool isActive) : base(id)
    {
        FullName = fullName;
        Role = role;
        IsActive = isActive;
    }

    public string FullName { get; }
    public StaffRole Role { get; }
    public bool IsActive { get; }

    public static StaffUser Create(Guid id, string fullName, StaffRole role)
    {
        Guard.Against.NullOrWhiteSpace(fullName, nameof(fullName));

        return new StaffUser(id, fullName, role, isActive: true);
    }
}
