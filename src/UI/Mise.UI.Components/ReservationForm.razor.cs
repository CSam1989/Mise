using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using Mise.UI.Abstractions;

namespace Mise.UI.Components;

public sealed partial class ReservationForm
{
    /// <summary>The mockup's exact copy — bUnit and the FluentValidation rule it mirrors both assert this string.</summary>
    internal const string PartySizeRequiredMessage = "Party size is required and must be more than 0.";

    /// <summary>Shown for anything the client throws — a network failure, an unreachable API,
    /// an unexpected 5xx — as opposed to <see cref="CreateReservationResult.ValidationErrors"/>,
    /// which is an ordinary, expected 400. Never the exception's own message: a raw
    /// exception message is an internal detail, not something a Floor Staff member mid-call
    /// should have to interpret.</summary>
    internal const string UnexpectedErrorMessage = "Something went wrong creating the reservation. Please try again.";

    [Parameter, EditorRequired]
    public IReservationsClient ReservationsClient { get; set; } = default!;

    [Parameter]
    public EventCallback<ReservationDto> OnCreated { get; set; }

    [Inject]
    private ILogger<ReservationForm> Logger { get; set; } = default!;

    private string CustomerName { get; set; } = string.Empty;
    private string CustomerPhone { get; set; } = string.Empty;
    private string PartySizeText { get; set; } = string.Empty;
    private DateTime? ReservationDateTimeLocal { get; set; }
    private Dictionary<string, string[]> FieldErrors { get; set; } = [];
    private string? UnexpectedError { get; set; }

    private async Task HandleSubmitAsync()
    {
        FieldErrors = [];
        UnexpectedError = null;

        if (!int.TryParse(PartySizeText, out var partySize) || partySize <= 0)
        {
            FieldErrors["PartySize"] = [PartySizeRequiredMessage];
            return;
        }

        var reservationDateTime = ReservationDateTimeLocal is { } localDateTime
            ? new DateTimeOffset(localDateTime)
            : DateTimeOffset.UtcNow;

        CreateReservationResult result;
        try
        {
            result = await ReservationsClient.CreateAsync(
                new CreateReservationRequest(Guid.NewGuid(), CustomerName, CustomerPhone, partySize, reservationDateTime),
                CancellationToken.None);
        }
        catch (Exception ex)
        {
            // A transport failure (API unreachable, an unexpected 5xx EnsureSuccessStatusCode
            // throws on) is not the same thing as a validation rejection — it must not crash
            // the circuit (Blazor Server's default for an unhandled component exception),
            // just show a generic message and let the user retry.
            //
            // Plain instance call, not [LoggerMessage]: that generator requires a field of
            // type ILogger, but Blazor's [Inject] only ever populates a property (its
            // component-property-injection reflects over PropertyInfo, not fields) — a real
            // conflict between the two source generators, not a shortcut. This handler isn't a
            // hot path either way (it only runs when a user submits and the call fails).
            Logger.LogError(ex, "Unexpected failure creating a reservation.");
            UnexpectedError = UnexpectedErrorMessage;
            return;
        }

        if (!result.IsSuccess)
        {
            FieldErrors = new Dictionary<string, string[]>(result.ValidationErrors);
            return;
        }

        CustomerName = string.Empty;
        CustomerPhone = string.Empty;
        PartySizeText = string.Empty;
        ReservationDateTimeLocal = null;

        await OnCreated.InvokeAsync(result.Reservation);
    }
}
