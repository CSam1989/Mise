using Microsoft.AspNetCore.Components;
using Mise.UI.Abstractions;

namespace Mise.UI.Components;

public sealed partial class ReservationForm
{
    /// <summary>The mockup's exact copy — bUnit and the FluentValidation rule it mirrors both assert this string.</summary>
    internal const string PartySizeRequiredMessage = "Party size is required and must be more than 0.";

    [Parameter, EditorRequired]
    public IReservationsClient ReservationsClient { get; set; } = default!;

    [Parameter]
    public EventCallback<ReservationDto> OnCreated { get; set; }

    private string CustomerName { get; set; } = string.Empty;
    private string PartySizeText { get; set; } = string.Empty;
    private DateTime? ReservationDateTimeLocal { get; set; }
    private Dictionary<string, string[]> FieldErrors { get; set; } = [];

    private async Task HandleSubmitAsync()
    {
        FieldErrors = [];

        if (!int.TryParse(PartySizeText, out var partySize) || partySize <= 0)
        {
            FieldErrors["PartySize"] = [PartySizeRequiredMessage];
            return;
        }

        var reservationDateTime = ReservationDateTimeLocal is { } localDateTime
            ? new DateTimeOffset(localDateTime)
            : DateTimeOffset.UtcNow;

        var result = await ReservationsClient.CreateAsync(
            new CreateReservationRequest(Guid.NewGuid(), CustomerName, partySize, reservationDateTime),
            CancellationToken.None);

        if (!result.IsSuccess)
        {
            FieldErrors = new Dictionary<string, string[]>(result.ValidationErrors);
            return;
        }

        CustomerName = string.Empty;
        PartySizeText = string.Empty;
        ReservationDateTimeLocal = null;

        await OnCreated.InvokeAsync(result.Reservation);
    }
}
