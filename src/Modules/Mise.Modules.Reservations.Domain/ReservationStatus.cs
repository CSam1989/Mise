namespace Mise.Modules.Reservations.Domain;

/// <summary>Only <see cref="Confirmed"/> exists until Phase 6/7 add Seated/Cancelled/NoShow.</summary>
public enum ReservationStatus
{
    Confirmed,
}
