using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Mise.Modules.Reservations.Application.Ports;
using Mise.Modules.Reservations.Contracts;
using Mise.Modules.Reservations.Domain;
using Mise.Modules.Tables.Contracts;
using Mise.SharedKernel;
using Mise.SharedKernel.Infrastructure;

namespace Mise.Modules.Reservations.Application.SeatReservation;

/// <summary>
/// FR-05/BR-04 — reuses <see cref="IReservationsData.UpdateReservationAsync"/> rather than a
/// dedicated gateway method: persisting a mutated <see cref="Reservation"/> under a concurrency
/// check with BR-01 re-validated is exactly what that method already does, and seating can both
/// reassign <see cref="Reservation.TableId"/> and change <see cref="ReservationStatus"/> to
/// Seated, either of which can create a genuine new overlap — unlike Cancel/MarkNoShow, which
/// never can (see <see cref="IReservationsData.CancelReservationAsync"/>'s doc comment).
/// ADR-007: the <see cref="ReservationSeated"/> cross-module event is published on every
/// <see cref="ReservationSaveOutcome.Saved"/> outcome, deliberately *not* gated by
/// <c>WasAlreadyProcessed</c> the way the audit write is below — see that event's doc comment
/// for why redispatching on replay is the wanted, self-healing behavior here.
/// </summary>
public sealed partial class SeatReservationCommandHandler(
    IReservationsData reservationsData,
    ITableAvailabilityLookup tableAvailabilityLookup,
    [FromKeyedServices(AuditWriterKeys.Reservations)] IAuditWriter auditWriter,
    IDomainEventPublisher domainEventPublisher,
    IRealtimeNotifier realtimeNotifier,
    IValidator<SeatReservationCommand> validator,
    TimeProvider timeProvider,
    ILogger<SeatReservationCommandHandler> logger)
{
    public async Task<ReservationSaveResult?> HandleAsync(SeatReservationCommand command, CancellationToken cancellationToken)
    {
        await validator.ValidateAndThrowAsync(command, cancellationToken);

        var current = await reservationsData.GetReservationByIdAsync(command.ReservationId, cancellationToken);
        if (current is null)
        {
            return null;
        }

        await TableAssignmentGuard.EnsureAssignmentIsValidAsync(
            command.TableId, current.Reservation.PartySize, tableAvailabilityLookup, cancellationToken);

        current.Reservation.MarkSeated(command.TableId, timeProvider.GetUtcNow());

        // Staged before the gateway call, unconditionally — see CreateReservationCommandHandler's
        // doc comment on this same pattern (AuditCompletenessInterceptor, Phase 9/ADR-009).
        auditWriter.Stage(new AuditLogEntry
        {
            Id = Guid.NewGuid(),
            EntityType = "Reservation",
            EntityId = command.ReservationId,
            Action = "Seated",
            PerformedByStaffId = command.PerformedByStaffId,
            OccurredAtUtc = timeProvider.GetUtcNow(),
            Details = string.Empty,
        });

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
            LogTableOverlap(command.TableId);
            throw new ReservationOverlapException(command.TableId);
        }

        if (result.WasAlreadyProcessed)
        {
            LogOperationReplayed(command.OperationId, command.ReservationId);
        }
        else
        {
            LogReservationSeated(command.ReservationId, command.TableId);

            // ADR-008: Seat is a status transition, not a distinct wire event — it notifies as
            // ReservationUpdated, same as any other field-level change, gated by
            // WasAlreadyProcessed same as the audit write (unlike the cross-module
            // ReservationSeated dispatch below, which deliberately redispatches on replay).
            await realtimeNotifier.NotifyReservationUpdatedAsync(
                new ReservationChangedNotification(
                    result.Reservation.Id, result.Reservation.CustomerName, result.Reservation.PartySize,
                    result.Reservation.ReservationDateTime, result.Reservation.Status.ToString(), result.Reservation.TableId),
                cancellationToken);
        }

        await domainEventPublisher.PublishAsync(
            new ReservationSeated(command.ReservationId, command.TableId, command.PerformedByStaffId, timeProvider.GetUtcNow()),
            cancellationToken);

        return result;
    }

    private static Dictionary<string, object?> CurrentStateOf(Reservation reservation) => new()
    {
        ["customerName"] = reservation.CustomerName,
        ["tableId"] = reservation.TableId,
        ["status"] = reservation.Status.ToString(),
    };

    [LoggerMessage(EventId = 14, Level = LogLevel.Information, Message = "Reservation {ReservationId} seated at table {TableId}.")]
    private partial void LogReservationSeated(Guid reservationId, Guid tableId);

    [LoggerMessage(EventId = 15, Level = LogLevel.Warning,
        Message = "OperationId {OperationId} was already processed for reservation {ReservationId}; skipping the audit write, but still redispatching the ReservationSeated event.")]
    private partial void LogOperationReplayed(Guid operationId, Guid reservationId);

    [LoggerMessage(EventId = 16, Level = LogLevel.Warning,
        Message = "Reservation {ReservationId} seat rejected: caller's version {ExpectedVersion} does not match current version {CurrentVersion}.")]
    private partial void LogVersionMismatch(Guid reservationId, uint expectedVersion, uint currentVersion);

    [LoggerMessage(EventId = 17, Level = LogLevel.Warning, Message = "BR-01 overlap rejected for table {TableId} on seat.")]
    private partial void LogTableOverlap(Guid tableId);
}
