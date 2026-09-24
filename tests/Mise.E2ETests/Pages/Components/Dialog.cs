using Mise.UI.Components;

namespace Mise.E2ETests.Pages.Components;

/// <summary>A Drawer or Modal instance, scoped to its own panel so its close button never collides with another overlay's.</summary>
public sealed class Dialog(IPage page, string testId)
{
    public ILocator Root => page.GetByTestId(testId);

    public ILocator Title => Root.GetByTestId(TestIds.OverlayTitle);

    public ILocator CloseButton => Root.GetByTestId(TestIds.Close);

    public Task CloseWithEscapeAsync() => page.Keyboard.PressAsync("Escape");

    /// <summary>Clicks the backdrop's top-left corner, well outside any panel.</summary>
    public Task CloseByClickingOutsideAsync() => page.Mouse.ClickAsync(5, 5);
}
