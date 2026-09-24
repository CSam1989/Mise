namespace Mise.E2ETests.Infrastructure;

internal static class PlaywrightBrowser
{
    public static async Task<(IPlaywright Playwright, IBrowser Browser)> LaunchAsync()
    {
        var playwright = await Playwright.CreateAsync();
        // Already Playwright's default; set explicitly because GetByTestId + TestIds is this suite's only locator vocabulary.
        playwright.Selectors.SetTestIdAttribute("data-testid");
        return (playwright, await playwright.Chromium.LaunchAsync());
    }
}
