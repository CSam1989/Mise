using Mise.UI.Components;

namespace Mise.E2ETests.Pages.Components;

/// <summary>The top bar, sidebar and toast outlet every signed-in page shares.</summary>
public sealed class AppShell(IPage page)
{
    public ILocator TopBar => page.GetByTestId(TestIds.TopBar);

    public ILocator RestaurantName => page.GetByTestId(TestIds.RestaurantName);

    public ILocator Date => page.GetByTestId(TestIds.ShellDate);

    public ILocator Clock => page.GetByTestId(TestIds.ShellClock);

    public ILocator CurrentUser => page.GetByTestId(TestIds.CurrentUser);

    public ILocator CurrentUserRole => page.GetByTestId(TestIds.CurrentUserRole);

    public ILocator Nav => page.GetByTestId(TestIds.SideNav);

    public ILocator NavReservations => page.GetByTestId(TestIds.NavReservations);

    public ILocator ManagerGroup => page.GetByTestId(TestIds.NavGroupManager);

    public ILocator SignOutLink => page.GetByTestId(TestIds.SignOut);

    public ILocator SkipLink => page.GetByTestId(TestIds.SkipToContent);

    public ILocator MainContent => page.GetByTestId(TestIds.MainContent);

    public ILocator ToastRegion => page.GetByTestId(TestIds.ToastRegion);

    public ILocator Toast => page.GetByTestId(TestIds.Toast);

    public async Task<LoginPage> SignOutAsync()
    {
        await SignOutLink.ClickAsync();
        return new LoginPage(page);
    }

    public async Task SkipToContentWithKeyboardAsync()
    {
        await SkipLink.FocusAsync();
        await page.Keyboard.PressAsync("Enter");
    }
}
