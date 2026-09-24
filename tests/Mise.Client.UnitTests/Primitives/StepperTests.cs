using Bunit;
using Microsoft.AspNetCore.Components;
using Mise.UI.Components;
using Mise.UI.Components.Primitives;

namespace Mise.Client.UnitTests.Primitives;

public class StepperTests : MiseComponentTestContext
{
    private IRenderedComponent<Stepper> RenderStepper(int value, Action<int>? onChange = null, int min = 1, int max = 12, bool disabled = false) =>
        Render<Stepper>(p => p
            .Add(c => c.Label, "Party")
            .Add(c => c.TestId, "stepper-party")
            .Add(c => c.Value, value)
            .Add(c => c.Min, min)
            .Add(c => c.Max, max)
            .Add(c => c.Disabled, disabled)
            .Add(c => c.ValueChanged, EventCallback.Factory.Create<int>(this, v => onChange?.Invoke(v))));

    [Fact]
    public void Render_Value_ShowsIt()
    {
        var cut = RenderStepper(4);

        cut.Find($"[data-testid={TestIds.StepperValue}]").TextContent.Should().Be("4");
    }

    [Fact]
    public void ClickIncrease_RaisesValuePlusOne()
    {
        int? changed = null;
        var cut = RenderStepper(4, v => changed = v);

        cut.Find($"[data-testid={TestIds.StepperIncrease}]").Click();

        changed.Should().Be(5);
    }

    [Fact]
    public void ClickDecrease_RaisesValueMinusOne()
    {
        int? changed = null;
        var cut = RenderStepper(4, v => changed = v);

        cut.Find($"[data-testid={TestIds.StepperDecrease}]").Click();

        changed.Should().Be(3);
    }

    [Fact]
    public void Render_AtMinimum_DisablesDecreaseOnly()
    {
        var cut = RenderStepper(1);

        cut.Find($"[data-testid={TestIds.StepperDecrease}]").HasAttribute("disabled").Should().BeTrue(because: "a party can never go below the minimum.");
        cut.Find($"[data-testid={TestIds.StepperIncrease}]").HasAttribute("disabled").Should().BeFalse();
    }

    [Fact]
    public void Render_AtMaximum_DisablesIncreaseOnly()
    {
        var cut = RenderStepper(12);

        cut.Find($"[data-testid={TestIds.StepperIncrease}]").HasAttribute("disabled").Should().BeTrue(because: "the stepper must not step past its maximum.");
        cut.Find($"[data-testid={TestIds.StepperDecrease}]").HasAttribute("disabled").Should().BeFalse();
    }

    [Fact]
    public void Render_Disabled_DisablesBothButtons()
    {
        var cut = RenderStepper(4, disabled: true);

        cut.FindAll("button").Should().OnlyContain(b => b.HasAttribute("disabled"));
    }

    [Fact]
    public void Render_Buttons_HaveExactAccessibleNames()
    {
        var cut = RenderStepper(4);

        cut.Find($"[data-testid={TestIds.StepperDecrease}]").GetAttribute("aria-label").Should().Be("Decrease Party");
        cut.Find($"[data-testid={TestIds.StepperIncrease}]").GetAttribute("aria-label").Should().Be("Increase Party");
    }

    [Fact]
    public void Render_DutchCulture_UsesDutchAccessibleNames()
    {
        UseCulture("nl-BE");

        var cut = RenderStepper(4);

        cut.Find($"[data-testid={TestIds.StepperIncrease}]").GetAttribute("aria-label").Should().Be("Party verhogen");
    }
}
