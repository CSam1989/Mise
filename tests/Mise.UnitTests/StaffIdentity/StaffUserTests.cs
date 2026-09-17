using Mise.Modules.StaffIdentity.Domain;

namespace Mise.UnitTests.StaffIdentity;

public class StaffUserTests
{
    [Fact]
    public void Create_EmptyFullName_Throws()
    {
        var act = () => StaffUser.Create(Guid.NewGuid(), "", StaffRole.FloorStaff);

        act.Should().Throw<ArgumentException>(
            because: "FullName is required — the one invariant this aggregate enforces (Phase 3, mirrors Reservation's own restraint).");
    }

    [Fact]
    public void Create_WhitespaceFullName_Throws()
    {
        var act = () => StaffUser.Create(Guid.NewGuid(), "   ", StaffRole.FloorStaff);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_ValidInput_SetsProperties()
    {
        var id = Guid.NewGuid();

        var staffUser = StaffUser.Create(id, "Jane Doe", StaffRole.Manager);

        staffUser.Id.Should().Be(id);
        staffUser.FullName.Should().Be("Jane Doe");
        staffUser.Role.Should().Be(StaffRole.Manager);
        staffUser.IsActive.Should().BeTrue(because: "a newly created staff account starts active.");
    }
}
