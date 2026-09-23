using Microsoft.Extensions.Logging;
using Mise.Modules.Reservations.Contracts;
using Mise.Modules.Tables.Application.Ports;
using Mise.Modules.Tables.Domain;
using Mise.SharedKernel;
using Mise.SharedKernel.Infrastructure;

namespace Mise.Modules.Tables.Application;

/// <summary>
/// BR-05's "unless another active reservation holds it" clause is decided here, via
/// <see cref="IReservationLookup"/> (ADR-007) — <see cref="Table.ReleaseIfReservationHeld"/>
/// itself only knows this aggregate's own Status field. Same idempotency/error-handling shape as
/// <see cref="ReservationSeatedTableOccupiedHandler"/> — see that type's doc comment.
/// </summary>
public sealed partial class ReservationTableVacatedHandler(
    ITablesData tablesData,
    IReservationLookup reservationLookup,
    IAuditWriter auditWriter,
    IRealtimeNotifier realtimeNotifier,
    TimeProvider timeProvider,
    ILogger<ReservationTableVacatedHandler> logger)
    : IDomainEventHandler<ReservationTableVacated>
{
    public async Task HandleAsync(ReservationTableVacated domainEvent, CancellationToken cancellationToken)
    {
        var current = await tablesData.GetTableByIdAsync(domainEvent.TableId, cancellationToken);
        if (current is null)
        {
            LogTableNotFound(domainEvent.TableId, domainEvent.ReservationId);
            return;
        }

        var now = timeProvider.GetUtcNow();
        var stillHeld = await reservationLookup.HasCurrentActiveReservationForTableAsync(
            domainEvent.TableId, domainEvent.ReservationId, now, cancellationToken);
        if (stillHeld)
        {
            LogStillHeld(domainEvent.TableId, domainEvent.ReservationId);
            return;
        }

        if (!current.Table.ReleaseIfReservationHeld())
        {
            LogNotReservationDriven(domainEvent.TableId, current.Table.Status);
            return;
        }

        var result = await tablesData.ChangeTableStatusAsync(
            current.Table, current.Version, Guid.NewGuid(), cancellationToken);

        if (result.Outcome == TableSaveOutcome.VersionMismatch)
        {
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
                Action = "Released",
                PerformedByStaffId = domainEvent.PerformedByStaffId,
                OccurredAtUtc = now,
                Details = $"Reservation {domainEvent.ReservationId} vacated.",
            },
            cancellationToken);
        LogTableReleased(domainEvent.TableId, domainEvent.ReservationId);

        await realtimeNotifier.NotifyTableStatusChangedAsync(
            new TableStatusChangedNotification(domainEvent.TableId, current.Table.Status.ToString()), cancellationToken);
    }

    [LoggerMessage(EventId = 80, Level = LogLevel.Information, Message = "Table {TableId} released back to Available after reservation {ReservationId} vacated it.")]
    private partial void LogTableReleased(Guid tableId, Guid reservationId);

    [LoggerMessage(EventId = 81, Level = LogLevel.Debug,
        Message = "Table {TableId} kept as-is: reservation {ReservationId} vacated it, but another active reservation currently holds it.")]
    private partial void LogStillHeld(Guid tableId, Guid reservationId);

    [LoggerMessage(EventId = 82, Level = LogLevel.Debug,
        Message = "Table {TableId} status {CurrentStatus} was not reservation-driven (Reserved/Occupied); leaving a staff-driven override untouched.")]
    private partial void LogNotReservationDriven(Guid tableId, TableStatus currentStatus);

    [LoggerMessage(EventId = 83, Level = LogLevel.Warning,
        Message = "Table {TableId} not found while handling a ReservationTableVacated event for reservation {ReservationId}.")]
    private partial void LogTableNotFound(Guid tableId, Guid reservationId);

    [LoggerMessage(EventId = 84, Level = LogLevel.Warning,
        Message = "Table {TableId} concurrency conflict while releasing — a concurrent write raced this handler; propagating so a client retry can heal it.")]
    private partial void LogVersionMismatch(Guid tableId);
}
