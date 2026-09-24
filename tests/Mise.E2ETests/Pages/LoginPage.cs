using Mise.E2ETests.Infrastructure;
using Mise.E2ETests.Pages.Components;
using Mise.UI.Components;

namespace Mise.E2ETests.Pages;

public sealed class LoginPage(IPage page)
{
    public const string Path = "/login";

    public ILocator Card => page.GetByTestId(TestIds.AuthCard);

    public ILocator Heading => page.GetByTestId(TestIds.LoginHeading);

    public ILocator RestaurantName => page.GetByTestId(TestIds.RestaurantName);

    public ILocator Username => page.GetByTestId(TestIds.InputUsername);

    public ILocator Password => page.GetByTestId(TestIds.InputPassword);

    public ILocator Submit => page.GetByTestId(TestIds.LoginSubmit);

    public ILocator Error => page.GetByTestId(TestIds.LoginError);

    public async Task<LoginPage> GotoAsync(string? returnUrl = null)
    {
        await page.GotoAsync(returnUrl is null ? Path : $"{Path}?returnUrl={Uri.EscapeDataString(returnUrl)}");
        return this;
    }

    public async Task SubmitAsync(string username, string password)
    {
        await Username.FillAsync(username);
        await Password.FillAsync(password);
        await Submit.ClickAsync();
    }

    /// <summary>Signs in from the current page and waits until the post-login full reload has rendered the shell.</summary>
    public async Task<AppShell> SignInAsync(E2EUser user)
    {
        await SubmitAsync(user.Username, user.Password);
        var shell = new AppShell(page);
        await shell.CurrentUser.WaitForAsync();
        return shell;
    }
}
