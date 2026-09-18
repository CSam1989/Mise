namespace Mise.Modules.Reservations.Contracts;

/// <summary>
/// <see cref="Version"/> is the caller-facing xmin token (docs/plan.md correction #5, extended
/// to Reservation in Phase 6 now that Update/Cancel give it something to protect) —
/// round-tripped as an <c>If-Match</c> header on the next PATCH, same shape as
/// <c>Mise.Modules.Tables.Contracts.TableDto</c>.
/// </summary>
public sealed record ReservationDto(
    Guid Id,
    string CustomerName,
    string CustomerPhone,
    string? CustomerEmail,
    int PartySize,
    DateTimeOffset ReservationDateTime,
    int DurationMinutes,
    string Status,
    Guid? TableId,
    string? Notes,
    Guid CreatedByStaffId,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    uint Version);
