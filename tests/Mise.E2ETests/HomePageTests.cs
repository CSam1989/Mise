namespace Mise.E2ETests;

[Trait("Category", "E2E")]
[Collection(PlaywrightCollection.Name)]
public class HomePageTests(PlaywrightWebAppFixture fixture) : BrowserTest(fixture)
{
    [Fact]
    public async Task Home_Unauthenticated_RedirectsToTheBackOfficeLoginCard()
    {
        var page = await OpenPageAsync();

        await page.GotoAsync("/");

        var login = new LoginPage(page);
        await Assertions.Expect(login.Card).ToBeVisibleAsync();
        await Assertions.Expect(login.Heading).ToHaveRoleAsync(AriaRole.Heading);
        await Assertions.Expect(login.RestaurantName).ToBeVisibleAsync();
        await Assertions.Expect(new AppShell(page).Nav).ToHaveCountAsync(0);
    }
}
