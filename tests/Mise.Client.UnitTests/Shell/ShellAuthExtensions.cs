using System.Security.Claims;
using Bunit;
using Bunit.TestDoubles;
using Mise.UI.Abstractions;

namespace Mise.Client.UnitTests.Shell;

internal static class ShellAuthExtensions
{
    public static readonly Guid StaffId = Guid.Parse("7d3b2a70-1c4e-4f7e-9b0a-0f5c3e1d2a11");

    public static void SignInAsManager(this BunitAuthorizationContext auth, string name = "Sam Verhoeven")
    {
        auth.SetAuthorized(name);
        auth.SetRoles(StaffRoles.Manager);
        auth.SetPolicies(StaffPolicies.Manager, StaffPolicies.FloorStaff);
        auth.SetClaims(new Claim(ClaimTypes.NameIdentifier, StaffId.ToString()));
    }

    public static void SignInAsFloorStaff(this BunitAuthorizationContext auth, string name = "Noor El Amrani")
    {
        auth.SetAuthorized(name);
        auth.SetRoles(StaffRoles.FloorStaff);
        auth.SetPolicies(StaffPolicies.FloorStaff);
        auth.SetClaims(new Claim(ClaimTypes.NameIdentifier, StaffId.ToString()));
    }
}
