namespace Mise.Modules.Reservations.Application.UpdateReservation;

/// <summary>Full-replace semantics, same as Tables' UpdateTableCommand — the caller resends
/// every editable field, including DurationMinutes (no defaulting on edit, unlike Create).</summary>
public sealed record UpdateReservationCommand(
    Guid OperationId,
    Guid ReservationId,
    uint ExpectedVersion,
    string CustomerName,
    string CustomerPhone,
    string? CustomerEmail,
    int PartySize,
    DateTimeOffset ReservationDateTime,
    int DurationMinutes,
    Guid? TableId,
    string? Notes,
    Guid PerformedByStaffId);
