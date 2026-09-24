using Bunit;
using Mise.UI.Components;
using Mise.UI.Components.Shell;

namespace Mise.Client.UnitTests.Shell;

public class AppShellTests : MiseComponentTestContext
{
    private IRenderedComponent<AppShell> RenderShell() =>
        Render<AppShell>(p => p
            .Add(c => c.RestaurantName, "Lilshof")
            .Add(c => c.TimeZone, TimeZoneInfo.FindSystemTimeZoneById("Europe/Brussels"))
            .Add(c => c.SignOutHref, "logout")
            .AddChildContent("<p data-testid=\"page-body\">Body</p>"));

    [Fact]
    public void Render_ComposesTopBarSideNavContentAndToasts()
    {
        Auth.SignInAsManager();

        var cut = RenderShell();

        cut.FindAll($"[data-testid={TestIds.TopBar}]").Should().HaveCount(1);
        cut.FindAll($"[data-testid={TestIds.SideNav}]").Should().HaveCount(1);
        cut.FindAll($"[data-testid={TestIds.ToastRegion}]").Should().HaveCount(1, because: "the shell hosts the single toast outlet every page shares.");
        cut.Find($"[data-testid={TestIds.MainContent}] [data-testid=page-body]").TextContent.Should().Be("Body");
    }

    [Fact]
    public void Render_PassesTheRestaurantNameThroughToTheTopBar()
    {
        Auth.SignInAsManager();

        var cut = RenderShell();

        cut.Find($"[data-testid={TestIds.RestaurantName}]").TextContent.Should().Be("Lilshof",
            because: "a bare string attribute on a Razor component is a literal, so a missing @ would render the parameter's own name.");
    }

    [Fact]
    public void ClickSkipLink_FocusesMainInsteadOfNavigating()
    {
        Auth.SignInAsFloorStaff();
        var cut = RenderShell();

        cut.Find($"[data-testid={TestIds.SkipToContent}]").Click();

        JSInterop.VerifyFocusAsyncInvoke().Arguments[0].Should().BeOfType<Microsoft.AspNetCore.Components.ElementReference>(
            because: "with <base href=\"/\"> a plain #fragment link would route to the home page instead of skipping the nav.");
    }

    [Fact]
    public void Render_SkipLink_TargetsTheMainLandmark()
    {
        Auth.SignInAsFloorStaff();

        var cut = RenderShell();

        cut.Find($"[data-testid={TestIds.SkipToContent}]").GetAttribute("href").Should().Be("#main-content");
        cut.Find($"[data-testid={TestIds.MainContent}]").Id.Should().Be("main-content");
        cut.Find($"[data-testid={TestIds.SkipToContent}]").TextContent.Should().Be("Skip to main content");
    }
}
