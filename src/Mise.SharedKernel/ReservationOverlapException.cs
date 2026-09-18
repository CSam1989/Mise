namespace Mise.SharedKernel;

/// <summary>
/// Thrown by an Application command handler when a gateway's
/// <c>ReservationSaveOutcome.TableOverlap</c> reports that the assigned table already has an
/// overlapping active reservation (docs/plan.md correction #1, BR-01: "A table cannot be linked
/// to two reservations whose time ranges overlap"). Enforced authoritatively by a Postgres
/// exclusion constraint (a TOCTOU-safe guarantee no application-level check alone can give); the
/// gateway also runs a friendly pre-check so most callers hit this clean exception rather than a
/// raw constraint-violation error. Lives in Mise.SharedKernel, not Reservations.Application,
/// because it has to cross the Application → Mise.ApiService boundary the same way
/// ConcurrencyConflictException already does — Mise.ApiService's
/// ReservationOverlapExceptionHandler is the one place that turns it into the 409.
/// </summary>
public sealed class ReservationOverlapException(Guid tableId)
    : Exception($"Table '{tableId}' already has an overlapping reservation for the requested time.")
{
    public Guid TableId { get; } = tableId;
}
