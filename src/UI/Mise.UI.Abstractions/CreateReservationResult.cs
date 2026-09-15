namespace Mise.UI.Abstractions;

/// <summary>
/// Transport-agnostic outcome: a bUnit test mocking <see cref="IReservationsClient"/> never
/// needs to know whether a real failure would have been an HTTP 400 or something else.
/// </summary>
public sealed class CreateReservationResult
{
    public ReservationDto? Reservation { get; }
    public IReadOnlyDictionary<string, string[]> ValidationErrors { get; }
    public bool IsSuccess => Reservation is not null;

    private CreateReservationResult(ReservationDto? reservation, IReadOnlyDictionary<string, string[]> validationErrors)
    {
        Reservation = reservation;
        ValidationErrors = validationErrors;
    }

    public static CreateReservationResult Succeeded(ReservationDto reservation) =>
        new(reservation, new Dictionary<string, string[]>());

    public static CreateReservationResult Failed(IReadOnlyDictionary<string, string[]> validationErrors) =>
        new(null, validationErrors);
}
