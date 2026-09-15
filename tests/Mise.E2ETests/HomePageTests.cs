namespace Mise.E2ETests;

[Trait("Category", "E2E")]
[Collection(PlaywrightCollection.Name)]
public class HomePageTests(PlaywrightWebAppFixture fixture)
{
    [Fact]
    public async Task Home_Opened_ShowsHeadingByDataTestId()
    {
        // Traces are always captured and always saved under playwright-traces/ (gitignored);
        // CI uploads that whole directory only when the job fails, per docs/plan.md's CI
        // design table — cheap enough to always record given there is only one test today.
        await using var context = await fixture.Browser.NewContextAsync();
        await context.Tracing.StartAsync(new TracingStartOptions { Screenshots = true, Snapshots = true });

        try
        {
            var page = await context.NewPageAsync();
            await page.GotoAsync(fixture.BaseUrl);

            var heading = page.Locator("[data-testid=home-heading]");
            await Assertions.Expect(heading).ToBeVisibleAsync();
            (await heading.TextContentAsync()).Should().Be("Hello, world!");
        }
        finally
        {
            Directory.CreateDirectory("playwright-traces");
            await context.Tracing.StopAsync(new TracingStopOptions
            {
                Path = Path.Combine("playwright-traces", $"{nameof(Home_Opened_ShowsHeadingByDataTestId)}.zip"),
            });
        }
    }
}
