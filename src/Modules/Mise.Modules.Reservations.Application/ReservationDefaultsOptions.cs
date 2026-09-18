namespace Mise.Modules.Reservations.Application;

/// <summary>
/// Decision #7 ("safe as configurable defaults"): default turn time is a setting read from
/// configuration, not a hardcoded literal, so changing it later is a config edit, not a test
/// rewrite. Bound from the "Reservations" configuration section at the composition root.
/// </summary>
public sealed class ReservationDefaultsOptions
{
    public int DefaultDurationMinutes { get; set; } = 90;
}
