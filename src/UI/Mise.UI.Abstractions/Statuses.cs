namespace Mise.UI.Abstractions;

public enum TableStatus
{
    Available,
    Reserved,
    Occupied,
    NeedsCleaning,
    Blocked,
}

public enum ReservationStatus
{
    Confirmed,
    Seated,
    Completed,
    NoShow,
    Cancelled,
}
