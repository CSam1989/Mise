using Microsoft.Extensions.Logging;
using Mise.Modules.Reservations.Contracts;
using Mise.Modules.Tables.Application.Ports;
using Mise.SharedKernel;
using Mise.SharedKernel.Infrastructure;

namespace Mise.Modules.Tables.Application;

/// <summary>
/// ADR-007 / the charter's own §10 example ("Reservation Seated → Table Occupied"). Not a
/// <c>*CommandHandler</c> (it is triggered by a cross-module domain event, not a REST request),
/// so CrossCuttingTests' IAuditWriter/ILogger constructor-dependency rules don't apply to it by
/// name — both are still taken here as a deliberate choice, for the same reasons those rules
/// exist for command handlers: FR-09/NFR-02 audit completeness and CLAUDE.md's logging standard
/// don't stop applying just because the trigger wasn't an HTTP request.
/// <para>
/// Generates a fresh <see cref="Guid"/> operationId on every call rather than threading one
/// through from the Reservation side: <c>shared.processed_operation</c>'s idempotency table
/// exists to protect against a *client* retrying the same HTTP request twice, which doesn't
/// apply to this in-process call — idempotency here is instead structural
/// (<see cref="Domain.Table.MarkOccupied"/> reports whether it actually changed anything, and
/// this handler skips the persist/audit write entirely when it didn't).
/// </para>
/// </summary>
public sealed partial class ReservationSeatedTableOccupiedHandler(
    ITablesData tablesData, IAuditWriter auditWriter, TimeProvider timeProvider, ILogger<ReservationSeatedTableOccupiedHandler> logger)
    : IDomainEventHandler<ReservationSeated>
{
    public async Task HandleAsync(ReservationSeated domainEvent, CancellationToken cancellationToken)
    {
        var current = await tablesData.GetTableByIdAsync(domainEvent.TableId, cancellationToken);
        if (current is null)
        {
            // The table was checked to exist by TableAssignmentGuard moments earlier in the same
            // request — this can only mean a genuine race (e.g. a concurrent hard-delete, which
            // nothing in this codebase actually does yet). Log and move on rather than throwing:
            // the Reservation itself is already correctly Seated, and there is no table left to
            // mark Occupied.
            LogTableNotFound(domainEvent.TableId, domainEvent.ReservationId);
            return;
        }

        if (!current.Table.MarkOccupied())
        {
            LogAlreadyOccupied(domainEvent.TableId);
            return;
        }

        var result = await tablesData.ChangeTableStatusAsync(
            current.Table, current.Version, Guid.NewGuid(), cancellationToken);

        if (result.Outcome == TableSaveOutcome.VersionMismatch)
        {
            // Unlike the table-not-found case above, this is transient and retry-recoverable:
            // throwing surfaces it as a loud failure of the whole /seat request (correction #10 —
            // never silently swallow a cross-module side effect), and a client retry on the same
            // OperationId redispatches this event (see ReservationSeated's doc comment), which
            // heals it once the race has passed.
            LogVersionMismatch(domainEvent.TableId);
            throw new ConcurrencyConflictException(
                "Table", domainEvent.TableId, result.Version, new Dictionary<string, object?> { ["status"] = result.Table.Status.ToString() });
        }

        await auditWriter.WriteAsync(
            new AuditLogEntry
            {
                Id = Guid.NewGuid(),
                EntityType = "Table",
                EntityId = domainEvent.TableId,
                Action = "Occupied",
                PerformedByStaffId = domainEvent.PerformedByStaffId,
                OccurredAtUtc = timeProvider.GetUtcNow(),
                Details = $"Reservation {domainEvent.ReservationId} seated.",
            },
            cancellationToken);
        LogTableOccupied(domainEvent.TableId, domainEvent.ReservationId);
    }

    [LoggerMessage(EventId = 76, Level = LogLevel.Information, Message = "Table {TableId} marked Occupied by reservation {ReservationId}.")]
    private partial void LogTableOccupied(Guid tableId, Guid reservationId);

    [LoggerMessage(EventId = 77, Level = LogLevel.Debug, Message = "Table {TableId} was already Occupied; skipping the redundant persist/audit write.")]
    private partial void LogAlreadyOccupied(Guid tableId);

    [LoggerMessage(EventId = 78, Level = LogLevel.Warning,
        Message = "Table {TableId} not found while handling a ReservationSeated event for reservation {ReservationId} — likely a genuine race, since TableAssignmentGuard already checked it exists earlier in the same request.")]
    private partial void LogTableNotFound(Guid tableId, Guid reservationId);

    [LoggerMessage(EventId = 79, Level = LogLevel.Warning,
        Message = "Table {TableId} concurrency conflict while marking Occupied — a concurrent write raced this handler; propagating so a client retry can heal it.")]
    private partial void LogVersionMismatch(Guid tableId);
}
