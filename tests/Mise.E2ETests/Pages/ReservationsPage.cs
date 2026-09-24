using Mise.E2ETests.Pages.Components;
using Mise.UI.Components;

namespace Mise.E2ETests.Pages;

public sealed class ReservationsPage(IPage page)
{
    public const string Path = "/reservations";

    public AppShell Shell { get; } = new(page);

    public ILocator Title => page.GetByTestId(TestIds.PageTitle);

    public ILocator CustomerName => page.GetByTestId(TestIds.InputCustomerName);

    public ILocator CustomerPhone => page.GetByTestId(TestIds.InputCustomerPhone);

    public ILocator PartySize => page.GetByTestId(TestIds.InputPartySize);

    public ILocator Submit => page.GetByTestId(TestIds.SubmitReservation);

    public ILocator DayList => page.GetByTestId(TestIds.DayList);

    public async Task<ReservationsPage> GotoAsync()
    {
        await page.GotoAsync(Path);
        return this;
    }

    public async Task CreateReservationAsync(string customerName, string customerPhone, int partySize)
    {
        await CustomerName.FillAsync(customerName);
        await CustomerPhone.FillAsync(customerPhone);
        await PartySize.FillAsync(partySize.ToString(System.Globalization.CultureInfo.InvariantCulture));
        await Submit.ClickAsync();
    }
}
