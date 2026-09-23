using Microsoft.Extensions.Logging;
using Mise.Modules.Reservations.Application.Ports;
using Mise.Modules.Reservations.Contracts;
using Mise.Modules.Reservations.Domain;
using Mise.SharedKernel;
using Mise.SharedKernel.Infrastructure;

namespace Mise.Modules.Reservations.Application.MarkReservationNoShow;

/// <summary>
/// No FluentValidation validator — same reasoning as CancelReservationCommandHandler's doc
/// comment: no user-typed business fields, only ids/version already shaped by the endpoint.
/// BR-05's other terminal, table-releasing transition (Cancel is the first) — publishes the same
/// <see cref="ReservationTableVacated"/> event Cancel does, only when the reservation actually
/// had a table assigned, on every Saved outcome including a replay (see that event's doc comment
/// for why).
/// </summary>
public sealed partial class MarkReservationNoShowCommandHandler(
    IReservationsData reservationsData,
    IAuditWriter auditWriter,
    IDomainEventPublisher domainEventPublisher,
    IRealtimeNotifier realtimeNotifier,
    TimeProvider timeProvider,
    ILogger<MarkReservationNoShowCommandHandler> logger)
{
    public async Task<ReservationSaveResult?> HandleAsync(MarkReservationNoShowCommand command, CancellationToken cancellationToken)
    {
        var current = await reservationsData.GetReservationByIdAsync(command.ReservationId, cancellationToken);
        if (current is null)
        {
            return null;
        }

        var tableId = current.Reservation.TableId;
        current.Reservation.MarkNoShow(timeProvider.GetUtcNow());

        var result = await reservationsData.MarkNoShowAsync(
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
            await auditWriter.WriteAsync(
                new AuditLogEntry
                {
                    Id = Guid.NewGuid(),
                    EntityType = "Reservation",
                    EntityId = command.ReservationId,
                    Action = "NoShow",
                    PerformedByStaffId = command.PerformedByStaffId,
                    OccurredAtUtc = timeProvider.GetUtcNow(),
                    Details = string.Empty,
                },
                cancellationToken);
            LogReservationNoShow(command.ReservationId);

            // ADR-008: NoShow, like Seat, notifies as ReservationUpdated (a status transition).
            await realtimeNotifier.NotifyReservationUpdatedAsync(
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

    [LoggerMessage(EventId = 18, Level = LogLevel.Information, Message = "Reservation {ReservationId} marked no-show.")]
    private partial void LogReservationNoShow(Guid reservationId);

    [LoggerMessage(EventId = 19, Level = LogLevel.Warning,
        Message = "OperationId {OperationId} was already processed for reservation {ReservationId}; skipping the no-show audit write.")]
    private partial void LogOperationReplayed(Guid operationId, Guid reservationId);

    [LoggerMessage(EventId = 20, Level = LogLevel.Warning,
        Message = "Reservation {ReservationId} no-show rejected: caller's version {ExpectedVersion} does not match current version {CurrentVersion}.")]
    private partial void LogVersionMismatch(Guid reservationId, uint expectedVersion, uint currentVersion);
}
