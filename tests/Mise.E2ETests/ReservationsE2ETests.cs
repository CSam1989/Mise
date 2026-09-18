namespace Mise.E2ETests;

[Trait("Category", "E2E")]
[Collection(MiseE2ECollection.Name)]
public class ReservationsE2ETests(MiseE2EFixture fixture)
{
    [Fact]
    public async Task CreateReservation_HappyPath_AppearsInDayList()
    {
        await using var context = await fixture.Browser.NewContextAsync();
        await context.Tracing.StartAsync(new TracingStartOptions { Screenshots = true, Snapshots = true });

        try
        {
            var page = await context.NewPageAsync();
            await LoginSteps.SignInAsync(page, fixture.BaseUrl, MiseE2EFixture.SeedManagerUsername, MiseE2EFixture.SeedManagerPassword);

            await page.GotoAsync($"{fixture.BaseUrl}/reservations");

            await page.Locator("[data-testid=input-customer-name]").FillAsync("Jane Doe");
            await page.Locator("[data-testid=input-customer-phone]").FillAsync("+32 470 00 00 00");
            await page.Locator("[data-testid=input-party-size]").FillAsync("4");
            await page.Locator("[data-testid=btn-submit-reservation]").ClickAsync();

            var dayList = page.Locator("[data-testid=day-list]");
            await Assertions.Expect(dayList).ToBeVisibleAsync();
            await Assertions.Expect(dayList).ToContainTextAsync("Jane Doe");
        }
        finally
        {
            Directory.CreateDirectory("playwright-traces");
            await context.Tracing.StopAsync(new TracingStopOptions
            {
                Path = Path.Combine("playwright-traces", $"{nameof(CreateReservation_HappyPath_AppearsInDayList)}.zip"),
            });
        }
    }
}
