using FluentValidation;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Mise.Modules.Reservations.Application.Ports;
using Mise.Modules.Reservations.Domain;
using Mise.Modules.Tables.Contracts;
using Mise.SharedKernel;
using Mise.SharedKernel.Infrastructure;

namespace Mise.Modules.Reservations.Application.CreateReservation;

/// <summary>
/// Invalid input throws FluentValidation's own <see cref="ValidationException"/> (400) or —
/// for BR-07 — <see cref="TableAssignmentGuard"/>'s field-scoped one. A BR-01 overlap on the
/// assigned table throws <see cref="ReservationOverlapException"/> (409), translated from the
/// gateway's <see cref="ReservationSaveOutcome.TableOverlap"/>.
/// </summary>
public sealed partial class CreateReservationCommandHandler(
    IReservationsData reservationsData,
    ITableAvailabilityLookup tableAvailabilityLookup,
    IAuditWriter auditWriter,
    IRealtimeNotifier realtimeNotifier,
    IValidator<CreateReservationCommand> validator,
    IOptions<ReservationDefaultsOptions> options,
    TimeProvider timeProvider,
    ILogger<CreateReservationCommandHandler> logger)
{
    public async Task<ReservationSaveResult> HandleAsync(CreateReservationCommand command, CancellationToken cancellationToken)
    {
        await validator.ValidateAndThrowAsync(command, cancellationToken);
        await TableAssignmentGuard.EnsureAssignmentIsValidAsync(
            command.TableId, command.PartySize, tableAvailabilityLookup, cancellationToken);

        var now = timeProvider.GetUtcNow();
        var durationMinutes = command.DurationMinutes ?? options.Value.DefaultDurationMinutes;

        var reservation = Reservation.Create(
            Guid.NewGuid(), command.CustomerName, command.CustomerPhone, command.CustomerEmail, command.PartySize,
            command.ReservationDateTime, durationMinutes, command.TableId, command.Notes, command.PerformedByStaffId, now);

        var result = await reservationsData.CreateReservationAsync(reservation, command.OperationId, cancellationToken);

        if (result.Outcome == ReservationSaveOutcome.TableOverlap)
        {
            LogTableOverlap(command.TableId!.Value);
            throw new ReservationOverlapException(command.TableId!.Value);
        }

        if (result.WasAlreadyProcessed)
        {
            LogOperationReplayed(command.OperationId, result.Reservation.Id);
        }
        else
        {
            // CustomerName is deliberately not passed to AuditLogEntry.Details as free text —
            // redacted the same way plan.md's retention corrections require for audit/conflict
            // payloads generally, not just this one call site.
            await auditWriter.WriteAsync(
                new AuditLogEntry
                {
                    Id = Guid.NewGuid(),
                    EntityType = "Reservation",
                    EntityId = result.Reservation.Id,
                    Action = "Created",
                    PerformedByStaffId = command.PerformedByStaffId,
                    OccurredAtUtc = now,
                    Details = $"Party of {command.PartySize}.",
                },
                cancellationToken);
            LogReservationCreated(result.Reservation.Id, command.PartySize);

            await realtimeNotifier.NotifyReservationCreatedAsync(
                new ReservationChangedNotification(
                    result.Reservation.Id, result.Reservation.CustomerName, result.Reservation.PartySize,
                    result.Reservation.ReservationDateTime, result.Reservation.Status.ToString(), result.Reservation.TableId),
                cancellationToken);
        }

        return result;
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Information,
        Message = "Reservation {ReservationId} created for a party of {PartySize}.")]
    private partial void LogReservationCreated(Guid reservationId, int partySize);

    [LoggerMessage(EventId = 2, Level = LogLevel.Warning,
        Message = "OperationId {OperationId} was already processed; returning existing reservation {ReservationId} instead of creating a new one.")]
    private partial void LogOperationReplayed(Guid operationId, Guid reservationId);

    [LoggerMessage(EventId = 3, Level = LogLevel.Warning,
        Message = "BR-01 overlap rejected for table {TableId} on create.")]
    private partial void LogTableOverlap(Guid tableId);
}
