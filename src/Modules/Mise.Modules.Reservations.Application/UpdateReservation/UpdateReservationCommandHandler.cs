using FluentValidation;
using Microsoft.Extensions.Logging;
using Mise.Modules.Reservations.Application.Ports;
using Mise.Modules.Reservations.Domain;
using Mise.Modules.Tables.Contracts;
using Mise.SharedKernel;
using Mise.SharedKernel.Infrastructure;

namespace Mise.Modules.Reservations.Application.UpdateReservation;

/// <summary>
/// Returns null for a missing <see cref="UpdateReservationCommand.ReservationId"/> (same shape
/// as UpdateTableCommandHandler). <see cref="Reservation.UpdateDetails"/> itself throws
/// <see cref="DomainRuleViolationException"/> if the reservation isn't Confirmed (409) — a
/// self-contained Domain invariant, checked before BR-01/BR-07 even run, since there is nothing
/// to re-validate against a reservation that can no longer be edited.
/// </summary>
public sealed partial class UpdateReservationCommandHandler(
    IReservationsData reservationsData,
    ITableAvailabilityLookup tableAvailabilityLookup,
    IAuditWriter auditWriter,
    IRealtimeNotifier realtimeNotifier,
    IValidator<UpdateReservationCommand> validator,
    TimeProvider timeProvider,
    ILogger<UpdateReservationCommandHandler> logger)
{
    public async Task<ReservationSaveResult?> HandleAsync(UpdateReservationCommand command, CancellationToken cancellationToken)
    {
        await validator.ValidateAndThrowAsync(command, cancellationToken);

        var current = await reservationsData.GetReservationByIdAsync(command.ReservationId, cancellationToken);
        if (current is null)
        {
            return null;
        }

        await TableAssignmentGuard.EnsureAssignmentIsValidAsync(
            command.TableId, command.PartySize, tableAvailabilityLookup, cancellationToken);

        current.Reservation.UpdateDetails(
            command.CustomerName, command.CustomerPhone, command.CustomerEmail, command.PartySize,
            command.ReservationDateTime, command.DurationMinutes, command.TableId, command.Notes,
            timeProvider.GetUtcNow());

        var result = await reservationsData.UpdateReservationAsync(
            current.Reservation, command.ExpectedVersion, command.OperationId, cancellationToken);

        if (result.Outcome == ReservationSaveOutcome.VersionMismatch)
        {
            LogVersionMismatch(command.ReservationId, command.ExpectedVersion, result.Version);
            throw new ConcurrencyConflictException(
                "Reservation", command.ReservationId, result.Version, CurrentStateOf(result.Reservation));
        }

        if (result.Outcome == ReservationSaveOutcome.TableOverlap)
        {
            LogTableOverlap(command.TableId!.Value);
            throw new ReservationOverlapException(command.TableId!.Value);
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
                    Action = "Updated",
                    PerformedByStaffId = command.PerformedByStaffId,
                    OccurredAtUtc = timeProvider.GetUtcNow(),
                    Details = $"Party of {command.PartySize}.",
                },
                cancellationToken);
            LogReservationUpdated(command.ReservationId);

            await realtimeNotifier.NotifyReservationUpdatedAsync(
                new ReservationChangedNotification(
                    result.Reservation.Id, result.Reservation.CustomerName, result.Reservation.PartySize,
                    result.Reservation.ReservationDateTime, result.Reservation.Status.ToString(), result.Reservation.TableId),
                cancellationToken);
        }

        return result;
    }

    private static Dictionary<string, object?> CurrentStateOf(Reservation reservation) => new()
    {
        ["customerName"] = reservation.CustomerName,
        ["partySize"] = reservation.PartySize,
        ["reservationDateTime"] = reservation.ReservationDateTime,
        ["durationMinutes"] = reservation.DurationMinutes,
        ["tableId"] = reservation.TableId,
        ["status"] = reservation.Status.ToString(),
    };

    [LoggerMessage(EventId = 4, Level = LogLevel.Information, Message = "Reservation {ReservationId} updated.")]
    private partial void LogReservationUpdated(Guid reservationId);

    [LoggerMessage(EventId = 5, Level = LogLevel.Warning,
        Message = "OperationId {OperationId} was already processed for reservation {ReservationId}; skipping the update.")]
    private partial void LogOperationReplayed(Guid operationId, Guid reservationId);

    [LoggerMessage(EventId = 6, Level = LogLevel.Warning,
        Message = "Reservation {ReservationId} update rejected: caller's version {ExpectedVersion} does not match current version {CurrentVersion}.")]
    private partial void LogVersionMismatch(Guid reservationId, uint expectedVersion, uint currentVersion);

    [LoggerMessage(EventId = 7, Level = LogLevel.Warning,
        Message = "BR-01 overlap rejected for table {TableId} on update.")]
    private partial void LogTableOverlap(Guid tableId);
}
