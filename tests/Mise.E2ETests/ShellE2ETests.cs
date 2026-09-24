using System.Text.RegularExpressions;

namespace Mise.E2ETests;

[Trait("Category", "E2E")]
[Collection(MiseE2ECollection.Name)]
public partial class ShellE2ETests(MiseE2EFixture fixture) : BrowserTest(fixture)
{
    [GeneratedRegex(@"^\d{2}:\d{2}$")]
    private static partial Regex ClockFormat();

    [GeneratedRegex("nav-link-active")]
    private static partial Regex ActiveNavClass();

    [Fact]
    public async Task Manager_OpensHome_LandsOnReservationsInsideTheShell()
    {
        var page = await OpenPageAsync(fixture.ManagerSession);

        await page.GotoAsync("/");

        var reservations = new ReservationsPage(page);
        var shell = reservations.Shell;
        await Assertions.Expect(page).ToHaveURLAsync($"{BaseUrl}{ReservationsPage.Path}");
        await Assertions.Expect(reservations.Title).ToHaveRoleAsync(AriaRole.Heading);
        await Assertions.Expect(shell.NavReservations).ToHaveClassAsync(ActiveNavClass());
        await Assertions.Expect(shell.RestaurantName).ToHaveTextAsync("Lilshof");
        await Assertions.Expect(shell.Clock).ToHaveTextAsync(ClockFormat());
        await Assertions.Expect(shell.CurrentUser).ToHaveTextAsync("E2E M.");
        await Assertions.Expect(shell.CurrentUserRole).ToHaveTextAsync("Manager");
    }

    [Fact]
    public async Task Shell_Landmarks_ExposeTheirRolesAndNames()
    {
        var page = await OpenPageAsync(fixture.FloorStaffSession);

        var shell = (await new ReservationsPage(page).GotoAsync()).Shell;

        await Assertions.Expect(shell.Nav).ToHaveRoleAsync(AriaRole.Navigation);
        await Assertions.Expect(shell.Nav).ToHaveAccessibleNameAsync("Main navigation");
        await Assertions.Expect(shell.MainContent).ToHaveRoleAsync(AriaRole.Main);
        await Assertions.Expect(shell.ToastRegion).ToHaveRoleAsync(AriaRole.Status);
        await Assertions.Expect(shell.ToastRegion).ToHaveAccessibleNameAsync("Notifications");
    }

    [Fact]
    public async Task FloorStaff_SeesFloorStaffRoleAndNoManagerGroup()
    {
        var page = await OpenPageAsync(fixture.FloorStaffSession);

        var shell = (await new ReservationsPage(page).GotoAsync()).Shell;

        await Assertions.Expect(shell.CurrentUser).ToHaveTextAsync("Noor A.");
        await Assertions.Expect(shell.CurrentUserRole).ToHaveTextAsync("Floor staff");
        await Assertions.Expect(shell.NavReservations).ToBeVisibleAsync();
        await Assertions.Expect(shell.ManagerGroup).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task DutchBrowser_SeesTheDutchShell()
    {
        var page = await OpenPageAsync(fixture.FloorStaffSession, locale: "nl-BE", width: 1024, height: 768);

        var shell = (await new ReservationsPage(page).GotoAsync()).Shell;

        await Assertions.Expect(shell.NavReservations).ToHaveTextAsync("Reservaties");
        await Assertions.Expect(shell.CurrentUserRole).ToHaveTextAsync("Zaalpersoneel");
        await Assertions.Expect(shell.SignOutLink).ToHaveTextAsync("Afmelden");
        await Assertions.Expect(shell.Nav).ToHaveAccessibleNameAsync("Hoofdnavigatie");
    }

    [Fact]
    public async Task SkipLink_Focused_EnterMovesFocusToMainContentWithoutNavigating()
    {
        var page = await OpenPageAsync(fixture.FloorStaffSession);
        var reservations = await new ReservationsPage(page).GotoAsync();
        await Assertions.Expect(reservations.Title).ToBeVisibleAsync();

        await reservations.Shell.SkipToContentWithKeyboardAsync();

        await Assertions.Expect(reservations.Shell.MainContent).ToBeFocusedAsync();
        await Assertions.Expect(page).ToHaveURLAsync($"{BaseUrl}{ReservationsPage.Path}");
    }
}
