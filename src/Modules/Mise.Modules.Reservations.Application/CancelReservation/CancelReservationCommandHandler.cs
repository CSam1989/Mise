using Microsoft.Extensions.Logging;
using Mise.Modules.Reservations.Application.Ports;
using Mise.Modules.Reservations.Domain;
using Mise.SharedKernel;
using Mise.SharedKernel.Infrastructure;

namespace Mise.Modules.Reservations.Application.CancelReservation;

/// <summary>
/// No FluentValidation validator — same reasoning as DeactivateTableCommandHandler's doc
/// comment: there are no user-typed business fields to validate, only ids/version already
/// shaped by the endpoint. <see cref="Reservation.Cancel"/> is idempotent by itself (a no-op if
/// already Cancelled), so this never throws a Domain-level rejection the way UpdateDetails can.
/// </summary>
public sealed partial class CancelReservationCommandHandler(
    IReservationsData reservationsData,
    IAuditWriter auditWriter,
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

        current.Reservation.Cancel(timeProvider.GetUtcNow());

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
            await auditWriter.WriteAsync(
                new AuditLogEntry
                {
                    Id = Guid.NewGuid(),
                    EntityType = "Reservation",
                    EntityId = command.ReservationId,
                    Action = "Cancelled",
                    PerformedByStaffId = command.PerformedByStaffId,
                    OccurredAtUtc = timeProvider.GetUtcNow(),
                    Details = string.Empty,
                },
                cancellationToken);
            LogReservationCancelled(command.ReservationId);
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
