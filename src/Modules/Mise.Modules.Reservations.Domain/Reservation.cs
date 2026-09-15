using Mise.SharedKernel;

namespace Mise.Modules.Reservations.Domain;

/// <summary>
/// The walking-skeleton slice of the Reservation aggregate (docs/plan.md Phase 2) — just
/// enough to prove the seam end-to-end. BR-01 overlap and BR-07 capacity/combinable rules
/// land in Phase 6; this factory enforces only the one invariant Phase 2 scopes in.
/// </summary>
public sealed class Reservation : AggregateRoot<Guid>
{
    private Reservation(
        Guid id, string customerName, int partySize, DateTimeOffset reservationDateTime, ReservationStatus status)
        : base(id)
    {
        CustomerName = customerName;
        PartySize = partySize;
        ReservationDateTime = reservationDateTime;
        Status = status;
    }

    public string CustomerName { get; }
    public int PartySize { get; }
    public DateTimeOffset ReservationDateTime { get; }
    public ReservationStatus Status { get; }

    public static Reservation Create(Guid id, string customerName, int partySize, DateTimeOffset reservationDateTime)
    {
        Guard.Against.NullOrWhiteSpace(customerName, nameof(customerName));
        Guard.Against.NegativeOrZero(partySize, nameof(partySize));

        return new Reservation(id, customerName, partySize, reservationDateTime, ReservationStatus.Confirmed);
    }
}
