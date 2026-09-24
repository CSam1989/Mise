using Mise.UI.Abstractions;

namespace Mise.E2ETests.Infrastructure;

public sealed record E2EUser(string Username, string Password, string FullName, string Role);

/// <summary>Every account <see cref="MiseE2EFixture"/> seeds. <see cref="SignOut"/> exists only so the one test that signs
/// out can't invalidate the server-side API token behind the shared Manager/Floor Staff sessions.</summary>
public static class E2EUsers
{
    public static readonly E2EUser Manager = new("e2e-manager", "E2E-Manager-Password1", "E2E Manager", StaffRoles.Manager);

    public static readonly E2EUser FloorStaff = new("e2e-floor", "E2E-Floor-Password1", "Noor El Amrani", StaffRoles.FloorStaff);

    public static readonly E2EUser SignOut = new("e2e-signout", "E2E-SignOut-Password1", "Lars Wouters", StaffRoles.FloorStaff);
}
