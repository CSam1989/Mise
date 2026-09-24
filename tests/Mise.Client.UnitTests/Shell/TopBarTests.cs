using Bunit;
using Mise.UI.Components;
using Mise.UI.Components.Shell;

namespace Mise.Client.UnitTests.Shell;

public class TopBarTests : MiseComponentTestContext
{
    private IRenderedComponent<TopBar> RenderTopBar() =>
        Render<TopBar>(p => p
            .Add(c => c.RestaurantName, "Lilshof")
            .Add(c => c.TimeZone, TimeZoneInfo.FindSystemTimeZoneById("Europe/Brussels")));

    [Fact]
    public void Render_ShowsRestaurantNameAndClock()
    {
        Auth.SignInAsManager();

        var cut = RenderTopBar();

        cut.Find($"[data-testid={TestIds.RestaurantName}]").TextContent.Should().Be("Lilshof");
        cut.Find($"[data-testid={TestIds.ShellClock}]").TextContent.Should().Be("19:04");
    }

    [Fact]
    public void Render_Manager_ShowsShortNameInitialsAndRole()
    {
        Auth.SignInAsManager("Sam Verhoeven");

        var cut = RenderTopBar();

        cut.Find($"[data-testid={TestIds.CurrentUser}]").TextContent.Should().Be("Sam V.", because: "the mockup shows first name plus last initial.");
        cut.Find($"[data-testid={TestIds.CurrentUser}]").GetAttribute("title").Should().Be("Sam Verhoeven");
        cut.Find($"[data-testid={TestIds.UserAvatar}]").TextContent.Should().Be("SV");
        cut.Find($"[data-testid={TestIds.CurrentUserRole}]").TextContent.Should().Be("Manager");
    }

    [Fact]
    public void Render_FloorStaff_ShowsFloorStaffRole()
    {
        Auth.SignInAsFloorStaff();

        var cut = RenderTopBar();

        cut.Find($"[data-testid={TestIds.CurrentUserRole}]").TextContent.Should().Be("Floor staff");
    }

    [Fact]
    public void Render_FloorStaffDutch_ShowsDutchRole()
    {
        UseCulture("nl-BE");
        Auth.SignInAsFloorStaff();

        var cut = RenderTopBar();

        cut.Find($"[data-testid={TestIds.CurrentUserRole}]").TextContent.Should().Be("Zaalpersoneel");
    }

    [Fact]
    public void Render_Anonymous_HidesUserBlock()
    {
        var cut = RenderTopBar();

        cut.FindAll($"[data-testid={TestIds.UserBlock}]").Should().BeEmpty(because: "there is no one to show before sign-in.");
    }

    [Fact]
    public void Render_AvatarColour_IsOneOfThePalette()
    {
        Auth.SignInAsManager();

        var cut = RenderTopBar();

        cut.Find($"[data-testid={TestIds.UserAvatar}]").ClassList.Should().ContainMatch("mise-avatar-?");
    }
}
