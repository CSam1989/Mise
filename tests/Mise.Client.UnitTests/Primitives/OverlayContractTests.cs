using Bunit;
using Microsoft.AspNetCore.Components;
using Mise.UI.Components;
using Mise.UI.Components.Primitives;

namespace Mise.Client.UnitTests.Primitives;

/// <summary>Drawer and Modal share one behavioural contract; each derived class runs the whole suite.</summary>
public abstract class OverlayContractTests<TOverlay> : MiseComponentTestContext
    where TOverlay : OverlayBase
{
    private const string OverlayTestId = "overlay-under-test";

    protected IRenderedComponent<TOverlay> RenderOverlay(bool isOpen, Action? onClose = null, RenderFragment? footer = null) =>
        Render<TOverlay>(p => p
            .Add(c => c.IsOpen, isOpen)
            .Add(c => c.Title, "M12 · Main room")
            .Add(c => c.TestId, OverlayTestId)
            .Add(c => c.OnClose, EventCallback.Factory.Create(this, () => onClose?.Invoke()))
            .Add(c => c.Footer, footer)
            .AddChildContent("<p data-testid=\"overlay-body\">Body</p>"));

    [Fact]
    public void Render_Closed_RendersNothing()
    {
        var cut = RenderOverlay(isOpen: false);

        cut.Markup.Trim().Should().BeEmpty(because: "a closed overlay must leave no backdrop behind to block clicks.");
    }

    [Fact]
    public void Render_Open_ShowsTitleBodyAsLabelledModalDialog()
    {
        var cut = RenderOverlay(isOpen: true);

        var dialog = cut.Find($"[data-testid={OverlayTestId}]");
        dialog.GetAttribute("role").Should().Be("dialog");
        dialog.GetAttribute("aria-modal").Should().Be("true");
        var title = cut.Find($"[data-testid={TestIds.OverlayTitle}]");
        title.TextContent.Should().Be("M12 · Main room");
        dialog.GetAttribute("aria-labelledby").Should().Be(title.Id, because: "the dialog's accessible name is its visible title.");
        cut.Find("[data-testid=overlay-body]").TextContent.Should().Be("Body");
    }

    [Fact]
    public void Render_OpenWithoutFooter_OmitsFooter()
    {
        var cut = RenderOverlay(isOpen: true);

        cut.FindAll($"[data-testid={TestIds.OverlayFooter}]").Should().BeEmpty();
    }

    [Fact]
    public void Render_OpenWithFooter_ShowsFooter()
    {
        var cut = RenderOverlay(isOpen: true, footer: b => b.AddMarkupContent(0, "<button data-testid=\"btn-save\">Save</button>"));

        cut.Find($"[data-testid={TestIds.OverlayFooter}] [data-testid=btn-save]").Should().NotBeNull();
    }

    [Fact]
    public void ClickClose_RaisesOnClose()
    {
        var closed = false;
        var cut = RenderOverlay(isOpen: true, onClose: () => closed = true);

        cut.Find($"[data-testid={TestIds.Close}]").Click();

        closed.Should().BeTrue();
    }

    [Fact]
    public void Render_CloseButton_HasLocalizedAccessibleName()
    {
        var cut = RenderOverlay(isOpen: true);

        cut.Find($"[data-testid={TestIds.Close}]").GetAttribute("aria-label").Should().Be("Close",
            because: "an icon-only button needs an accessible name.");
    }

    [Fact]
    public void ClickBackdrop_RaisesOnClose()
    {
        var closed = false;
        var cut = RenderOverlay(isOpen: true, onClose: () => closed = true);

        cut.Find($"[data-testid={TestIds.Backdrop}]").Click();

        closed.Should().BeTrue(because: "clicking outside the panel dismisses it, as in the mockup.");
    }

    [Fact]
    public void ClickInsidePanel_DoesNotRaiseOnClose()
    {
        var closed = false;
        var cut = RenderOverlay(isOpen: true, onClose: () => closed = true);

        var click = () => cut.Find("[data-testid=overlay-body]").Click();

        click.Should().Throw<MissingEventHandlerException>(
            because: "bUnit's bubbling stops at the panel's stopPropagation, so no handler is reached — the backdrop's dismiss included.");
        closed.Should().BeFalse();
    }

    [Fact]
    public void PressEscape_RaisesOnClose()
    {
        var closed = false;
        var cut = RenderOverlay(isOpen: true, onClose: () => closed = true);

        cut.Find($"[data-testid={OverlayTestId}]").KeyDown("Escape");

        closed.Should().BeTrue(because: "Escape is the standard keyboard dismissal for a dialog.");
    }

    [Fact]
    public void PressOtherKey_DoesNotRaiseOnClose()
    {
        var closed = false;
        var cut = RenderOverlay(isOpen: true, onClose: () => closed = true);

        cut.Find($"[data-testid={OverlayTestId}]").KeyDown("Enter");

        closed.Should().BeFalse();
    }

    [Theory]
    [InlineData(TestIds.FocusSentinelStart)]
    [InlineData(TestIds.FocusSentinelEnd)]
    public void FocusSentinel_Focused_SendsFocusBackToThePanel(string sentinel)
    {
        var cut = RenderOverlay(isOpen: true);
        var focusCallsAfterOpen = JSInterop.Invocations.Count;

        cut.Find($"[data-testid={sentinel}]").Focus();

        JSInterop.Invocations.Count.Should().Be(focusCallsAfterOpen + 1,
            because: "tabbing off either edge of an open dialog must wrap back inside it, not into the page behind (WCAG 2.4.3).");
    }

    [Fact]
    public void Open_MovesFocusIntoThePanel()
    {
        var cut = RenderOverlay(isOpen: false);

        cut.Render(p => p.Add(c => c.IsOpen, true));

        JSInterop.VerifyFocusAsyncInvoke().Arguments[0].Should().BeOfType<ElementReference>(
            because: "opening a dialog must move keyboard focus into it so Escape and Tab work immediately.");
    }
}

public class DrawerTests : OverlayContractTests<Drawer>
{
}

public class ModalTests : OverlayContractTests<Modal>
{
    [Theory]
    [InlineData(ModalSize.Small, "modal-panel-small")]
    [InlineData(ModalSize.Medium, "modal-panel-medium")]
    [InlineData(ModalSize.Large, "modal-panel-large")]
    public void Render_Size_AppliesWidthClass(ModalSize size, string cssClass)
    {
        var cut = Render<Modal>(p => p
            .Add(c => c.IsOpen, true)
            .Add(c => c.Title, "New reservation")
            .Add(c => c.TestId, "modal-new")
            .Add(c => c.Size, size));

        cut.Find("[data-testid=modal-new]").ClassList.Should().Contain(cssClass);
    }
}
