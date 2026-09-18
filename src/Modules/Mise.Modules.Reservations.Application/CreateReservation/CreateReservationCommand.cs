namespace Mise.Modules.Reservations.Application.CreateReservation;

/// <summary>
/// <paramref name="DurationMinutes"/> is nullable — a null falls back to
/// <see cref="ReservationDefaultsOptions.DefaultDurationMinutes"/> (Decision #7). A null
/// <paramref name="TableId"/> is an unassigned reservation (charter: "a reservation may be
/// unassigned initially") — BR-01/BR-07 simply don't apply until one is set, here or via a
/// later edit. <paramref name="PerformedByStaffId"/> is resolved by the API endpoint from the
/// caller's JWT, same as Phase 2.
/// </summary>
public sealed record CreateReservationCommand(
    Guid OperationId,
    string CustomerName,
    string CustomerPhone,
    string? CustomerEmail,
    int PartySize,
    DateTimeOffset ReservationDateTime,
    int? DurationMinutes,
    Guid? TableId,
    string? Notes,
    Guid PerformedByStaffId);
