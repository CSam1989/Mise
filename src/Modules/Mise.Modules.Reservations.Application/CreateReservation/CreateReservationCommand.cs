namespace Mise.Modules.Reservations.Application.CreateReservation;

/// <summary>
/// <paramref name="PerformedByStaffId"/> is the authenticated staff member's id, resolved by
/// the API endpoint from the caller's JWT (StaffIdentity, Phase 3) — every caller of this
/// endpoint is a real, authenticated staff member under the global fallback policy, so there
/// is no system-process fallback to carry here the way Phase 2's placeholder spine needed.
/// </summary>
public sealed record CreateReservationCommand(
    Guid OperationId,
    string CustomerName,
    int PartySize,
    DateTimeOffset ReservationDateTime,
    Guid PerformedByStaffId);
