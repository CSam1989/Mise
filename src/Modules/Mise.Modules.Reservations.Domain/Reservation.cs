using Mise.SharedKernel;

namespace Mise.Modules.Reservations.Domain;

/// <summary>
/// Phase 6 ("Reservations proper") grows this past Phase 2's walking-skeleton shape: phone/
/// email/notes/table assignment/timestamps, plus <see cref="Cancel"/> (FR-03's "cancel").
/// BR-01 (overlap) and BR-07 (capacity/combinable) are cross-aggregate, DB-dependent checks —
/// they live in Application (CreateReservationCommandHandler/UpdateReservationCommandHandler),
/// not here, same categorization CLAUDE.md already draws for Table/Section. <see cref="Seated"/>/
/// <see cref="ReservationStatus.Completed"/>/<see cref="ReservationStatus.NoShow"/> stay
/// unreachable — Phase 7's concern.
/// </summary>
public sealed class Reservation : AggregateRoot<Guid>
{
    private Reservation(
        Guid id, string customerName, string customerPhone, string? customerEmail, int partySize,
        DateTimeOffset reservationDateTime, int durationMinutes, ReservationStatus status, Guid? tableId,
        string? notes, Guid createdByStaffId, DateTimeOffset createdAtUtc, DateTimeOffset updatedAtUtc)
        : base(id)
    {
        CustomerName = customerName;
        CustomerPhone = customerPhone;
        CustomerEmail = customerEmail;
        PartySize = partySize;
        ReservationDateTime = reservationDateTime;
        DurationMinutes = durationMinutes;
        Status = status;
        TableId = tableId;
        Notes = notes;
        CreatedByStaffId = createdByStaffId;
        CreatedAtUtc = createdAtUtc;
        UpdatedAtUtc = updatedAtUtc;
    }

    public string CustomerName { get; private set; }
    public string CustomerPhone { get; private set; }
    public string? CustomerEmail { get; private set; }
    public int PartySize { get; private set; }
    public DateTimeOffset ReservationDateTime { get; private set; }
    public int DurationMinutes { get; private set; }
    public ReservationStatus Status { get; private set; }
    public Guid? TableId { get; private set; }
    public string? Notes { get; private set; }
    public Guid CreatedByStaffId { get; }
    public DateTimeOffset CreatedAtUtc { get; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public static Reservation Create(
        Guid id, string customerName, string customerPhone, string? customerEmail, int partySize,
        DateTimeOffset reservationDateTime, int durationMinutes, Guid? tableId, string? notes,
        Guid createdByStaffId, DateTimeOffset nowUtc)
    {
        ValidateFields(customerName, customerPhone, partySize, durationMinutes);

        return new Reservation(
            id, customerName, customerPhone, customerEmail, partySize, reservationDateTime, durationMinutes,
            ReservationStatus.Confirmed, tableId, notes, createdByStaffId, nowUtc, nowUtc);
    }

    /// <summary>Self-contained Domain invariant (this aggregate's own Status field, no
    /// cross-aggregate query) — same category as <c>Table.Deactivate()</c>'s guard, so it
    /// throws <see cref="DomainRuleViolationException"/> (409), not a validator's 400. Editing a
    /// reservation that has already been cancelled — or, from Phase 7 on, seated/completed/
    /// no-showed — is a genuine business-rule violation, not a validation failure of the new
    /// field values themselves.</summary>
    public void UpdateDetails(
        string customerName, string customerPhone, string? customerEmail, int partySize,
        DateTimeOffset reservationDateTime, int durationMinutes, Guid? tableId, string? notes, DateTimeOffset nowUtc)
    {
        if (Status != ReservationStatus.Confirmed)
        {
            throw new DomainRuleViolationException($"Cannot edit a reservation while its status is {Status}.");
        }

        ValidateFields(customerName, customerPhone, partySize, durationMinutes);

        CustomerName = customerName;
        CustomerPhone = customerPhone;
        CustomerEmail = customerEmail;
        PartySize = partySize;
        ReservationDateTime = reservationDateTime;
        DurationMinutes = durationMinutes;
        TableId = tableId;
        Notes = notes;
        UpdatedAtUtc = nowUtc;
    }

    /// <summary>Idempotent no-op if already <see cref="ReservationStatus.Cancelled"/> — matching
    /// <c>Table.Deactivate()</c>'s own leniency (re-deactivating an already-inactive table is
    /// harmless there too). Only <see cref="ReservationStatus.Confirmed"/> is reachable before
    /// Phase 7, so there is no Seated/Completed/NoShow case to guard against yet — revisit when
    /// Phase 7 makes those reachable.</summary>
    public void Cancel(DateTimeOffset nowUtc)
    {
        if (Status == ReservationStatus.Cancelled)
        {
            return;
        }

        Status = ReservationStatus.Cancelled;
        UpdatedAtUtc = nowUtc;
    }

    private static void ValidateFields(string customerName, string customerPhone, int partySize, int durationMinutes)
    {
        Guard.Against.NullOrWhiteSpace(customerName, nameof(customerName));
        Guard.Against.NullOrWhiteSpace(customerPhone, nameof(customerPhone));
        Guard.Against.NegativeOrZero(partySize, nameof(partySize));
        Guard.Against.NegativeOrZero(durationMinutes, nameof(durationMinutes));
    }
}
