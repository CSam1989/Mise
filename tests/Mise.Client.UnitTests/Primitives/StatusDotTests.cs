using Bunit;
using Mise.UI.Abstractions;
using Mise.UI.Components;
using Mise.UI.Components.Primitives;

namespace Mise.Client.UnitTests.Primitives;

public class StatusDotTests : MiseComponentTestContext
{
    [Fact]
    public void Render_WithoutLabel_ExposesStatusAsAccessibleName()
    {
        var cut = Render<StatusDot>(p => p.Add(c => c.Status, TableStatus.Occupied));

        var dot = cut.Find(".status-dot");
        dot.GetAttribute("role").Should().Be("img");
        dot.GetAttribute("aria-label").Should().Be("Occupied", because: "a bare colour dot must not be the only carrier of meaning for a screen reader.");
        cut.Find($"[data-testid={TestIds.StatusDot}]").TextContent.Trim().Should().BeEmpty();
    }

    [Fact]
    public void Render_WithLabel_ShowsTextAndHidesDotFromAssistiveTech()
    {
        var cut = Render<StatusDot>(p => p
            .Add(c => c.Status, ReservationStatus.NoShow)
            .Add(c => c.ShowLabel, true));

        cut.Find($"[data-testid={TestIds.StatusDot}]").TextContent.Trim().Should().Be("No-show");
        cut.Find(".status-dot").GetAttribute("aria-hidden").Should().Be("true",
            because: "the visible label already names the status; announcing the dot too would say it twice.");
    }

    [Fact]
    public void Render_Status_AppliesItsColourSet()
    {
        var cut = Render<StatusDot>(p => p.Add(c => c.Status, TableStatus.Blocked));

        cut.Find($"[data-testid={TestIds.StatusDot}]").ClassList.Should().Contain("mise-status-blocked");
    }
}
