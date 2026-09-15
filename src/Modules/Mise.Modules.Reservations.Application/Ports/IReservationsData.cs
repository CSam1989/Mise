using Mise.Modules.Reservations.Domain;

namespace Mise.Modules.Reservations.Application.Ports;

/// <summary>
/// Slice-specific gateway (CLAUDE.md "The testable seam") — not a generic repository.
/// <see cref="CreateReservationAsync"/> is atomic with the OperationId idempotency check:
/// replaying the same <paramref name="operationId"/> returns the existing reservation id and
/// writes nothing new (docs/plan.md's "must land in Phase 2" guardrail — retrofitting this
/// later breaks both the wire contract and the schema once the offline Outbox needs it).
/// </summary>
public interface IReservationsData
{
    Task<CreateReservationResult> CreateReservationAsync(
        Reservation reservation, Guid operationId, CancellationToken cancellationToken);
}

public sealed record CreateReservationResult(Guid ReservationId, bool WasAlreadyProcessed);
