using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Mise.UI.Components;
using Mise.UI.Components.Feedback;

namespace Mise.Client.UnitTests.Feedback;

public class ToastHostTests : MiseComponentTestContext
{
    [Fact]
    public void Render_NoToast_ShowsEmptyPoliteLiveRegion()
    {
        var cut = Render<ToastHost>();

        var region = cut.Find($"[data-testid={TestIds.ToastRegion}]");
        region.GetAttribute("aria-live").Should().Be("polite", because: "toasts are announced without interrupting the screen reader.");
        region.GetAttribute("aria-label").Should().Be("Notifications");
        cut.FindAll($"[data-testid={TestIds.Toast}]").Should().BeEmpty();
    }

    [Fact]
    public void Show_RendersTheToastText()
    {
        var cut = Render<ToastHost>();

        cut.InvokeAsync(() => Services.GetRequiredService<ToastService>().Show("Willems, Karim → M3"));

        cut.WaitForAssertion(() => cut.Find($"[data-testid={TestIds.Toast}]").TextContent.Should().Be("Willems, Karim → M3"));
    }

    [Fact]
    public void Show_ErrorKind_AppliesErrorStyle()
    {
        var cut = Render<ToastHost>();

        cut.InvokeAsync(() => Services.GetRequiredService<ToastService>().Show("Could not save", ToastKind.Error));

        cut.WaitForAssertion(() => cut.Find($"[data-testid={TestIds.Toast}]").ClassList.Should().Contain("mise-toast-error"));
    }

    [Fact]
    public void Show_AfterDisplayDuration_DisappearsFromTheDom()
    {
        var cut = Render<ToastHost>();
        cut.InvokeAsync(() => Services.GetRequiredService<ToastService>().Show("Saved"));
        cut.WaitForAssertion(() => cut.FindAll($"[data-testid={TestIds.Toast}]").Should().HaveCount(1));

        Time.Advance(ToastService.DisplayDuration);

        cut.WaitForAssertion(() => cut.FindAll($"[data-testid={TestIds.Toast}]").Should().BeEmpty());
    }
}
