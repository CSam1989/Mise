using Mise.Modules.Reservations.Domain;

namespace Mise.Modules.Reservations.Application.Ports;

/// <summary>
/// Slice-specific gateway (CLAUDE.md "The testable seam"). <see cref="ReservationWithVersion"/>'s
/// Version is the Postgres <c>xmin</c> value (docs/plan.md correction #5, extended to
/// Reservation in Phase 6 — see <c>ReservationConfiguration</c>'s doc comment), same shape as
/// Tables' <c>TableWithVersion</c>. <see cref="ReservationSaveOutcome.TableOverlap"/> is BR-01's
/// gateway-level outcome (docs/plan.md correction #1): the implementation runs an
/// application-level pre-check for a friendly conflict message, and separately catches the
/// authoritative Postgres exclusion-constraint violation as the TOCTOU-safe fallback — either
/// one reaching this outcome is indistinguishable to the caller, which is the point.
/// </summary>
public interface IReservationsData
{
    Task<ReservationSaveResult> CreateReservationAsync(
        Reservation reservation, Guid operationId, CancellationToken cancellationToken);

    Task<ReservationWithVersion?> GetReservationByIdAsync(Guid reservationId, CancellationToken cancellationToken);

    /// <param name="reservation">Already loaded (via <see cref="GetReservationByIdAsync"/>) and
    /// mutated by the caller via a Domain method — this call persists it under an
    /// optimistic-concurrency check against <paramref name="expectedVersion"/>, same shape as
    /// Tables' <c>ITablesData.UpdateTableAsync</c>.</param>
    Task<ReservationSaveResult> UpdateReservationAsync(
        Reservation reservation, uint expectedVersion, Guid operationId, CancellationToken cancellationToken);

    /// <summary>Never produces <see cref="ReservationSaveOutcome.TableOverlap"/> — freeing or
    /// keeping the same table assignment while only flipping Status to Cancelled cannot create a
    /// new overlap.</summary>
    Task<ReservationSaveResult> CancelReservationAsync(
        Reservation reservation, uint expectedVersion, Guid operationId, CancellationToken cancellationToken);

    /// <summary>Phase 7/BR-05. Same "never produces TableOverlap" reasoning as
    /// <see cref="CancelReservationAsync"/> — flipping Status to NoShow without touching TableId
    /// or the time window cannot create a new overlap.</summary>
    Task<ReservationSaveResult> MarkNoShowAsync(
        Reservation reservation, uint expectedVersion, Guid operationId, CancellationToken cancellationToken);

    /// <summary>US-02/FR-02: <paramref name="query"/> matches CustomerName (pg_trgm-backed) or
    /// CustomerPhone (partial match), <paramref name="date"/> filters to that calendar date —
    /// either, both, or neither may be supplied. A plain filtered read injected directly into
    /// its endpoint, no separate query-handler class — same "no query-handler pattern exists yet
    /// for a plain projection" reasoning as Scheduling's
    /// <c>ISchedulingData.GetServicePeriodsForDateAsync</c>.</summary>
    Task<IReadOnlyList<ReservationWithVersion>> SearchReservationsAsync(
        string? query, DateOnly? date, CancellationToken cancellationToken);
}

public sealed record ReservationWithVersion(Reservation Reservation, uint Version);

public enum ReservationSaveOutcome
{
    Saved,
    VersionMismatch,
    TableOverlap,
}

public sealed record ReservationSaveResult(
    ReservationSaveOutcome Outcome, Reservation Reservation, uint Version, bool WasAlreadyProcessed);
