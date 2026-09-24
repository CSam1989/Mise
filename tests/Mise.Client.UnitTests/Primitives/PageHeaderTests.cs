using Bunit;
using Microsoft.AspNetCore.Components;
using Mise.UI.Components;
using Mise.UI.Components.Primitives;

namespace Mise.Client.UnitTests.Primitives;

public class PageHeaderTests : MiseComponentTestContext
{
    [Fact]
    public void Render_TitleOnly_OmitsSubtitleAndActions()
    {
        var cut = Render<PageHeader>(p => p.Add(c => c.Title, "Reservations"));

        cut.Find($"[data-testid={TestIds.PageTitle}]").TextContent.Should().Be("Reservations");
        cut.FindAll($"[data-testid={TestIds.PageSubtitle}]").Should().BeEmpty(because: "an absent subtitle must not leave an empty line in the header.");
        cut.FindAll($"[data-testid={TestIds.PageActions}]").Should().BeEmpty(because: "an absent actions slot must not render its container.");
    }

    [Fact]
    public void Render_WithSubtitle_ShowsIt()
    {
        var cut = Render<PageHeader>(p => p
            .Add(c => c.Title, "Floor plan")
            .Add(c => c.Subtitle, "18 free · 6 occupied"));

        cut.Find($"[data-testid={TestIds.PageSubtitle}]").TextContent.Should().Be("18 free · 6 occupied");
    }

    [Fact]
    public void Render_WithActions_RendersTheActionsFragment()
    {
        var cut = Render<PageHeader>(p => p
            .Add(c => c.Title, "Reservations")
            .Add(c => c.Actions, (RenderFragment)(b => b.AddMarkupContent(0, "<button data-testid=\"btn-new\">New</button>"))));

        cut.Find($"[data-testid={TestIds.PageActions}] [data-testid=btn-new]").Should().NotBeNull();
    }

    [Fact]
    public void Render_Title_IsTheFocusTargetH1()
    {
        var cut = Render<PageHeader>(p => p.Add(c => c.Title, "Reservations"));

        var title = cut.Find($"[data-testid={TestIds.PageTitle}]");
        title.TagName.Should().Be("H1", because: "Routes.razor's FocusOnNavigate targets the page's h1 after every navigation.");
        title.GetAttribute("tabindex").Should().Be("-1", because: "a heading only receives programmatic focus with tabindex=-1.");
    }
}
