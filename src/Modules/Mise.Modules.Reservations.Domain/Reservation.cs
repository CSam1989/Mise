using Mise.SharedKernel;

namespace Mise.Modules.Reservations.Domain;

/// <summary>
/// Phase 6 ("Reservations proper") grows this past Phase 2's walking-skeleton shape: phone/
/// email/notes/table assignment/timestamps, plus <see cref="Cancel"/> (FR-03's "cancel").
/// BR-01 (overlap) and BR-07 (capacity/combinable) are cross-aggregate, DB-dependent checks —
/// they live in Application (CreateReservationCommandHandler/UpdateReservationCommandHandler),
/// not here, same categorization CLAUDE.md already draws for Table/Section. Phase 7 adds
/// <see cref="MarkSeated"/> (FR-05/BR-04) and <see cref="MarkNoShow"/> (BR-05) —
/// <see cref="ReservationStatus.Completed"/> stays unreachable; no command drives it yet.
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
    /// harmless there too). Deliberately no guard against the prior status otherwise (a Seated
    /// party can still be cancelled) — same leniency BR-05 gets on the <see cref="MarkNoShow"/>
    /// side, and the charter names no restriction here either.</summary>
    public void Cancel(DateTimeOffset nowUtc)
    {
        if (Status == ReservationStatus.Cancelled)
        {
            return;
        }

        Status = ReservationStatus.Cancelled;
        UpdatedAtUtc = nowUtc;
    }

    /// <summary>FR-05/BR-04: "a reservation cannot be marked Seated without an assigned table" —
    /// enforced structurally here (<paramref name="tableId"/> is a required, non-empty
    /// <see cref="Guid"/>, not the nullable <see cref="TableId"/> field type), and by
    /// <c>SeatReservationCommandValidator</c> at the Application boundary (400, exact-message
    /// test contract) before this method is ever reached. Always overwrites
    /// <see cref="TableId"/> with the caller's value — seating is exactly the point a party can
    /// be reassigned to a different table than the one booked at Create/Update time, a normal
    /// restaurant workflow. Same-category guard as <c>UpdateDetails</c>: only a Confirmed
    /// reservation can be seated (Cancelled/NoShow/Completed all reject) — <b>except</b> an
    /// idempotent no-op when already Seated at the *same* <paramref name="tableId"/>, same
    /// leniency category as <see cref="Cancel"/>/<see cref="MarkNoShow"/>. This one is easy to
    /// miss because, unlike Cancel/MarkNoShow, the naive "only from Confirmed" guard reads as
    /// correct right up until an OperationId replay of an already-seated reservation hits it —
    /// caught by a failing integration test
    /// (<c>PatchReservationSeat_SameOperationIdTwice_SeatsOnlyOnce</c>) getting 409 on the second
    /// call, not by inspection. A *different* tableId while already Seated is a genuine conflict,
    /// not a replay, and still rejects.</summary>
    public void MarkSeated(Guid tableId, DateTimeOffset nowUtc)
    {
        if (tableId == Guid.Empty)
        {
            throw new ArgumentException("TableId must not be empty.", nameof(tableId));
        }

        if (Status == ReservationStatus.Seated && TableId == tableId)
        {
            return;
        }

        if (Status != ReservationStatus.Confirmed)
        {
            throw new DomainRuleViolationException($"Cannot seat a reservation while its status is {Status}.");
        }

        TableId = tableId;
        Status = ReservationStatus.Seated;
        UpdatedAtUtc = nowUtc;
    }

    /// <summary>BR-05's other terminal, table-releasing transition — idempotent no-op if already
    /// <see cref="ReservationStatus.NoShow"/>, same leniency as <see cref="Cancel"/>, and
    /// deliberately no guard against the prior status either (symmetry with Cancel: the charter
    /// names no restriction on which status a no-show can be marked from). Unlike
    /// <see cref="Cancel"/>, <see cref="TableId"/> is left untouched — both transitions keep the
    /// historical table assignment on the record; only the *table's* status is freed, handled at
    /// the Application layer via the cross-module <c>ReservationTableVacated</c> event.</summary>
    public void MarkNoShow(DateTimeOffset nowUtc)
    {
        if (Status == ReservationStatus.NoShow)
        {
            return;
        }

        Status = ReservationStatus.NoShow;
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
