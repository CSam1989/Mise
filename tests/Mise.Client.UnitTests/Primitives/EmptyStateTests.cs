using Bunit;
using Microsoft.AspNetCore.Components;
using Mise.UI.Components;
using Mise.UI.Components.Primitives;

namespace Mise.Client.UnitTests.Primitives;

public class EmptyStateTests : MiseComponentTestContext
{
    [Fact]
    public void Render_TitleOnly_OmitsBody()
    {
        var cut = Render<EmptyState>(p => p.Add(c => c.Title, "No reservations found"));

        cut.Find($"[data-testid={TestIds.EmptyStateTitle}]").TextContent.Should().Be("No reservations found");
        cut.FindAll($"[data-testid={TestIds.EmptyStateBody}]").Should().BeEmpty();
    }

    [Fact]
    public void Render_WithBody_ShowsIt()
    {
        var cut = Render<EmptyState>(p => p
            .Add(c => c.Title, "No reservations found")
            .Add(c => c.Body, "Try another day or search term."));

        cut.Find($"[data-testid={TestIds.EmptyStateBody}]").TextContent.Should().Be("Try another day or search term.");
    }

    [Fact]
    public void Render_WithAction_RendersIt()
    {
        var cut = Render<EmptyState>(p => p
            .Add(c => c.Title, "No reservations found")
            .Add(c => c.Action, (RenderFragment)(b => b.AddMarkupContent(0, "<button data-testid=\"btn-new\">New</button>"))));

        cut.Find($"[data-testid={TestIds.EmptyState}] [data-testid=btn-new]").Should().NotBeNull();
    }
}
