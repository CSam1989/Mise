using Bunit;
using Microsoft.AspNetCore.Components;
using Mise.UI.Components;
using Mise.UI.Components.Primitives;

namespace Mise.Client.UnitTests.Primitives;

public class SegmentedControlTests : MiseComponentTestContext
{
    private static readonly IReadOnlyList<SegmentedOption<string>> Options =
    [
        new("all", "all", "All"),
        new("patio", "patio", "Patio"),
    ];

    private IRenderedComponent<SegmentedControl<string>> RenderControl(string value, Action<string>? onChange = null) =>
        Render<SegmentedControl<string>>(p => p
            .Add(c => c.Options, Options)
            .Add(c => c.Value, value)
            .Add(c => c.AriaLabel, "Section")
            .Add(c => c.TestId, "segmented-section")
            .Add(c => c.ValueChanged, EventCallback.Factory.Create<string>(this, v => onChange?.Invoke(v))));

    [Fact]
    public void Render_Value_MarksOnlyThatOptionPressed()
    {
        var cut = RenderControl("patio");

        cut.Find($"[data-testid={TestIds.Segment("patio")}]").GetAttribute("aria-pressed").Should().Be("true");
        cut.Find($"[data-testid={TestIds.Segment("all")}]").GetAttribute("aria-pressed").Should().Be("false");
        cut.Find("[data-testid=segmented-section]").GetAttribute("role").Should().Be("group", because: "a toggle-button group keeps every option a plain tab stop; a radiogroup would owe arrow-key navigation.");
    }

    [Fact]
    public void ClickOtherOption_RaisesValueChanged()
    {
        string? changed = null;
        var cut = RenderControl("all", v => changed = v);

        cut.Find($"[data-testid={TestIds.Segment("patio")}]").Click();

        changed.Should().Be("patio");
    }

    [Fact]
    public void ClickSelectedOption_DoesNotRaiseValueChanged()
    {
        string? changed = null;
        var cut = RenderControl("all", v => changed = v);

        cut.Find($"[data-testid={TestIds.Segment("all")}]").Click();

        changed.Should().BeNull(because: "re-selecting the current option is not a change and must not trigger a reload upstream.");
    }

    [Fact]
    public void Render_Labels_ComeFromOptions()
    {
        var cut = RenderControl("all");

        cut.FindAll("button").Select(b => b.TextContent).Should().Equal("All", "Patio");
    }
}
