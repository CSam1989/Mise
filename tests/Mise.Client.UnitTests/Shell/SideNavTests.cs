using Bunit;
using Mise.UI.Components;
using Mise.UI.Components.Shell;

namespace Mise.Client.UnitTests.Shell;

public class SideNavTests : MiseComponentTestContext
{
    private static readonly IReadOnlyList<NavEntry> EntriesWithManagerItem =
    [
        new("reservations", "NavReservations", TestIds.NavReservations, NavGroup.Main),
        new("staff", "SignOut", "nav-test-manager-item", NavGroup.Manager),
    ];

    private IRenderedComponent<SideNav> RenderNav(IReadOnlyList<NavEntry>? entries = null) =>
        Render<SideNav>(p =>
        {
            p.Add(c => c.SignOutHref, "logout");
            if (entries is not null)
            {
                p.Add(c => c.Entries, entries);
            }
        });

    [Fact]
    public void Render_Default_ShowsOnlyScreensThatExist()
    {
        Auth.SignInAsManager();

        var cut = RenderNav();

        cut.FindAll("a[data-testid^=nav-]").Select(a => a.GetAttribute("data-testid")).Should().Equal(TestIds.NavReservations);
        cut.Find($"[data-testid={TestIds.NavReservations}]").TextContent.Should().Be("Reservations");
    }

    [Fact]
    public void Render_Manager_SeesManagerGroup()
    {
        Auth.SignInAsManager();

        var cut = RenderNav(EntriesWithManagerItem);

        cut.Find($"[data-testid={TestIds.NavGroupManager}]").TextContent.Should().Contain("Manager");
        cut.FindAll("[data-testid=nav-test-manager-item]").Should().HaveCount(1);
    }

    [Fact]
    public void Render_FloorStaff_DoesNotSeeManagerGroup()
    {
        Auth.SignInAsFloorStaff();

        var cut = RenderNav(EntriesWithManagerItem);

        cut.FindAll($"[data-testid={TestIds.NavGroupManager}]").Should().BeEmpty(because: "Manager screens are hidden from Floor Staff, not merely disabled.");
        cut.FindAll("[data-testid=nav-test-manager-item]").Should().BeEmpty();
        cut.FindAll($"[data-testid={TestIds.NavReservations}]").Should().HaveCount(1);
    }

    [Fact]
    public void Render_ManagerWithNoManagerEntries_HasNoEmptyGroupHeading()
    {
        Auth.SignInAsManager();

        var cut = RenderNav();

        cut.FindAll($"[data-testid={TestIds.NavGroupManager}]").Should().BeEmpty(because: "a heading with nothing under it is visual noise.");
    }

    [Fact]
    public void Render_SignOut_PointsAtTheHostsSignOutEndpoint()
    {
        Auth.SignInAsFloorStaff();

        var cut = RenderNav();

        var signOut = cut.Find($"[data-testid={TestIds.SignOut}]");
        signOut.GetAttribute("href").Should().Be("logout");
        signOut.TextContent.Should().Be("Sign out");
    }

    [Fact]
    public void Render_Dutch_LocalizesLabels()
    {
        UseCulture("nl-BE");
        Auth.SignInAsFloorStaff();

        var cut = RenderNav();

        cut.Find($"[data-testid={TestIds.NavReservations}]").TextContent.Should().Be("Reservaties");
        cut.Find($"[data-testid={TestIds.SignOut}]").TextContent.Should().Be("Afmelden");
        cut.Find($"[data-testid={TestIds.SideNav}]").GetAttribute("aria-label").Should().Be("Hoofdnavigatie");
    }
}
