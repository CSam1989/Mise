using System.Security.Claims;
using Mise.UI.Abstractions;
using Mise.UI.Components.Shell;

namespace Mise.Client.UnitTests.Shell;

public class StaffIdentityViewTests
{
    private static ClaimsPrincipal User(string name, string role, string? id = null)
    {
        var claims = new List<Claim> { new(ClaimTypes.Name, name), new(ClaimTypes.Role, role) };
        if (id is not null)
        {
            claims.Add(new Claim(ClaimTypes.NameIdentifier, id));
        }

        return new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
    }

    [Theory]
    [InlineData("Sam Verhoeven", "Sam V.", "SV")]
    [InlineData("Lieve de Peeters", "Lieve P.", "LP")]
    [InlineData("Noor", "Noor", "N")]
    [InlineData("  elke   vandenberghe  ", "elke V.", "EV")]
    public void From_Name_DerivesShortNameAndInitials(string fullName, string shortName, string initials)
    {
        var view = StaffIdentityView.From(User(fullName, StaffRoles.FloorStaff));

        view.ShortName.Should().Be(shortName);
        view.Initials.Should().Be(initials);
    }

    [Fact]
    public void From_ManagerRole_UsesManagerLabel()
    {
        StaffIdentityView.From(User("Sam V", StaffRoles.Manager)).RoleLabelKey.Should().Be("RoleManager");
    }

    [Fact]
    public void From_FloorStaffRole_UsesFloorStaffLabel()
    {
        StaffIdentityView.From(User("Noor", StaffRoles.FloorStaff)).RoleLabelKey.Should().Be("RoleFloorStaff");
    }

    [Fact]
    public void From_SameStaffId_AlwaysGetsTheSameAvatarColour()
    {
        var id = Guid.NewGuid().ToString();

        var first = StaffIdentityView.From(User("Sam V", StaffRoles.Manager, id)).AvatarIndex;
        var second = StaffIdentityView.From(User("Renamed Person", StaffRoles.Manager, id)).AvatarIndex;

        first.Should().Be(second, because: "the colour is keyed on the stable staff id, so a rename doesn't recolour someone.");
        first.Should().BeInRange(0, 5);
    }
}
