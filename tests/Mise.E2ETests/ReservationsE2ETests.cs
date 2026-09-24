namespace Mise.E2ETests;

[Trait("Category", "E2E")]
[Collection(MiseE2ECollection.Name)]
public class ReservationsE2ETests(MiseE2EFixture fixture) : BrowserTest(fixture)
{
    [Fact]
    public async Task CreateReservation_HappyPath_AppearsInDayList()
    {
        var page = await OpenPageAsync(fixture.ManagerSession);
        var reservations = await new ReservationsPage(page).GotoAsync();

        await reservations.CreateReservationAsync("Jane Doe", "+32 470 00 00 00", 4);

        await Assertions.Expect(reservations.DayList).ToBeVisibleAsync();
        await Assertions.Expect(reservations.DayList).ToContainTextAsync("Jane Doe");
    }
}
