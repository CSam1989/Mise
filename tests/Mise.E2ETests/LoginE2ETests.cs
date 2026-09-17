namespace Mise.E2ETests;

[Trait("Category", "E2E")]
[Collection(MiseE2ECollection.Name)]
public class LoginE2ETests(MiseE2EFixture fixture)
{
    [Fact]
    public async Task Login_InvalidCredentials_ShowsErrorAndStaysOnLoginPage()
    {
        await using var context = await fixture.Browser.NewContextAsync();
        var page = await context.NewPageAsync();

        await page.GotoAsync($"{fixture.BaseUrl}/login");
        await page.Locator("[data-testid=input-username]").FillAsync(MiseE2EFixture.SeedManagerUsername);
        await page.Locator("[data-testid=input-password]").FillAsync("wrong-password");
        await page.Locator("[data-testid=btn-login]").ClickAsync();

        await Assertions.Expect(page.Locator("[data-testid=error-login]")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("[data-testid=input-username]")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task UnauthenticatedVisit_ToReservations_RedirectsToLogin()
    {
        await using var context = await fixture.Browser.NewContextAsync();
        var page = await context.NewPageAsync();

        await page.GotoAsync($"{fixture.BaseUrl}/reservations");

        await Assertions.Expect(page.Locator("[data-testid=login-heading]")).ToBeVisibleAsync();
    }
}
