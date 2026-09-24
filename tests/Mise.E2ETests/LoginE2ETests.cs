namespace Mise.E2ETests;

[Trait("Category", "E2E")]
[Collection(MiseE2ECollection.Name)]
public class LoginE2ETests(MiseE2EFixture fixture) : BrowserTest(fixture)
{
    [Fact]
    public async Task Login_ValidCredentials_LandsOnReservationsInsideTheShell()
    {
        var page = await OpenPageAsync();
        var login = await new LoginPage(page).GotoAsync();

        var shell = await login.SignInAsync(E2EUsers.Manager);

        await Assertions.Expect(page).ToHaveURLAsync($"{BaseUrl}{ReservationsPage.Path}");
        await Assertions.Expect(shell.CurrentUser).ToHaveTextAsync("E2E M.");
    }

    [Fact]
    public async Task Login_InvalidCredentials_ShowsErrorAndStaysOnLoginPage()
    {
        var page = await OpenPageAsync();
        var login = await new LoginPage(page).GotoAsync();

        await login.SubmitAsync(E2EUsers.Manager.Username, "wrong-password");

        await Assertions.Expect(login.Error).ToBeVisibleAsync();
        await Assertions.Expect(login.Error).ToHaveRoleAsync(AriaRole.Alert);
        await Assertions.Expect(login.Username).ToBeVisibleAsync();
    }

    [Fact]
    public async Task UnauthenticatedVisit_ToReservations_RedirectsToLogin()
    {
        var page = await OpenPageAsync();

        await page.GotoAsync(ReservationsPage.Path);

        await Assertions.Expect(new LoginPage(page).Heading).ToBeVisibleAsync();
    }

    [Fact]
    public async Task Login_OffSiteReturnUrl_StaysOnMise()
    {
        var page = await OpenPageAsync();
        var login = await new LoginPage(page).GotoAsync(returnUrl: "https://example.com/phish");

        await login.SignInAsync(E2EUsers.Manager);

        await Assertions.Expect(page).ToHaveURLAsync($"{BaseUrl}{ReservationsPage.Path}");
    }

    [Fact]
    public async Task Login_SameOriginReturnUrl_ReturnsToTheRequestedPage()
    {
        var page = await OpenPageAsync();
        await page.GotoAsync(DesignGalleryPage.Path);
        var login = new LoginPage(page);
        await Assertions.Expect(login.Heading).ToBeVisibleAsync();

        await login.SignInAsync(E2EUsers.Manager);

        await Assertions.Expect(new DesignGalleryPage(page).Root).ToBeVisibleAsync();
    }

    [Fact]
    public async Task DutchBrowser_SeesTheDutchLoginCard()
    {
        var page = await OpenPageAsync(locale: "nl-BE");

        var login = await new LoginPage(page).GotoAsync();

        await Assertions.Expect(login.Heading).ToHaveTextAsync("Aanmelden bij Mise");
        await Assertions.Expect(login.RestaurantName).ToHaveTextAsync("Lilshof · Zaal");
        await Assertions.Expect(page.Locator("html")).ToHaveAttributeAsync("lang", "nl");
    }

    [Fact]
    public async Task SignOut_ReturnsToTheLoginCardAndProtectsPagesAgain()
    {
        var page = await OpenPageAsync();
        var shell = await (await new LoginPage(page).GotoAsync()).SignInAsync(E2EUsers.SignOut);

        var login = await shell.SignOutAsync();

        await Assertions.Expect(login.Heading).ToBeVisibleAsync();
        await page.GotoAsync(ReservationsPage.Path);
        await Assertions.Expect(login.Heading).ToBeVisibleAsync();
    }
}
