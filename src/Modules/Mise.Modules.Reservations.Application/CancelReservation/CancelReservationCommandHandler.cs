using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Mise.Modules.Reservations.Application.Ports;
using Mise.Modules.Reservations.Contracts;
using Mise.Modules.Reservations.Domain;
using Mise.SharedKernel;
using Mise.SharedKernel.Infrastructure;

namespace Mise.Modules.Reservations.Application.CancelReservation;

/// <summary>
/// No FluentValidation validator — same reasoning as DeactivateTableCommandHandler's doc
/// comment: there are no user-typed business fields to validate, only ids/version already
/// shaped by the endpoint. <see cref="Reservation.Cancel"/> is idempotent by itself (a no-op if
/// already Cancelled), so this never throws a Domain-level rejection the way UpdateDetails can.
/// BR-05 ("cancelling ... frees its table") is added in Phase 7 — publishes
/// <see cref="ReservationTableVacated"/> when the cancelled reservation had a table assigned, on
/// every Saved outcome including a replay (see that event's doc comment for why).
/// </summary>
public sealed partial class CancelReservationCommandHandler(
    IReservationsData reservationsData,
    [FromKeyedServices(AuditWriterKeys.Reservations)] IAuditWriter auditWriter,
    IDomainEventPublisher domainEventPublisher,
    IRealtimeNotifier realtimeNotifier,
    TimeProvider timeProvider,
    ILogger<CancelReservationCommandHandler> logger)
{
    public async Task<ReservationSaveResult?> HandleAsync(CancelReservationCommand command, CancellationToken cancellationToken)
    {
        var current = await reservationsData.GetReservationByIdAsync(command.ReservationId, cancellationToken);
        if (current is null)
        {
            return null;
        }

        var tableId = current.Reservation.TableId;
        current.Reservation.Cancel(timeProvider.GetUtcNow());

        // Staged before the gateway call, unconditionally — see CreateReservationCommandHandler's
        // doc comment on this same pattern (AuditCompletenessInterceptor, Phase 9/ADR-009).
        auditWriter.Stage(new AuditLogEntry
        {
            Id = Guid.NewGuid(),
            EntityType = "Reservation",
            EntityId = command.ReservationId,
            Action = "Cancelled",
            PerformedByStaffId = command.PerformedByStaffId,
            OccurredAtUtc = timeProvider.GetUtcNow(),
            Details = string.Empty,
        });

        var result = await reservationsData.CancelReservationAsync(
            current.Reservation, command.ExpectedVersion, command.OperationId, cancellationToken);

        if (result.Outcome == ReservationSaveOutcome.VersionMismatch)
        {
            LogVersionMismatch(command.ReservationId, command.ExpectedVersion, result.Version);
            throw new ConcurrencyConflictException(
                "Reservation", command.ReservationId, result.Version, CurrentStateOf(result.Reservation));
        }

        if (result.WasAlreadyProcessed)
        {
            LogOperationReplayed(command.OperationId, command.ReservationId);
        }
        else
        {
            LogReservationCancelled(command.ReservationId);

            await realtimeNotifier.NotifyReservationCancelledAsync(
                new ReservationChangedNotification(
                    result.Reservation.Id, result.Reservation.CustomerName, result.Reservation.PartySize,
                    result.Reservation.ReservationDateTime, result.Reservation.Status.ToString(), result.Reservation.TableId),
                cancellationToken);
        }

        if (tableId is { } id)
        {
            await domainEventPublisher.PublishAsync(
                new ReservationTableVacated(command.ReservationId, id, command.PerformedByStaffId, timeProvider.GetUtcNow()),
                cancellationToken);
        }

        return result;
    }

    private static Dictionary<string, object?> CurrentStateOf(Reservation reservation) => new()
    {
        ["customerName"] = reservation.CustomerName,
        ["status"] = reservation.Status.ToString(),
    };

    [LoggerMessage(EventId = 8, Level = LogLevel.Information, Message = "Reservation {ReservationId} cancelled.")]
    private partial void LogReservationCancelled(Guid reservationId);

    [LoggerMessage(EventId = 9, Level = LogLevel.Warning,
        Message = "OperationId {OperationId} was already processed for reservation {ReservationId}; skipping the cancellation.")]
    private partial void LogOperationReplayed(Guid operationId, Guid reservationId);

    [LoggerMessage(EventId = 10, Level = LogLevel.Warning,
        Message = "Reservation {ReservationId} cancel rejected: caller's version {ExpectedVersion} does not match current version {CurrentVersion}.")]
    private partial void LogVersionMismatch(Guid reservationId, uint expectedVersion, uint currentVersion);
}
