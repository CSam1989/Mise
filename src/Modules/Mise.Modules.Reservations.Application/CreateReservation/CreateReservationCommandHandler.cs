using FluentValidation;
using Microsoft.Extensions.Logging;
using Mise.Modules.Reservations.Application.Ports;
using Mise.Modules.Reservations.Domain;
using Mise.SharedKernel.Infrastructure;

namespace Mise.Modules.Reservations.Application.CreateReservation;

/// <summary>
/// Invalid input throws FluentValidation's own <see cref="ValidationException"/> (caught by
/// Mise.ApiService's ValidationExceptionHandler and turned into a 400 field-scoped
/// ValidationProblem) rather than returning a Result — this is the seam CLAUDE.md's handler
/// unit-test contract exercises directly (Times.Never on the gateway when it throws).
/// </summary>
public sealed partial class CreateReservationCommandHandler(
    IReservationsData reservationsData,
    IAuditWriter auditWriter,
    IValidator<CreateReservationCommand> validator,
    TimeProvider timeProvider,
    ILogger<CreateReservationCommandHandler> logger)
{
    public async Task<Guid> HandleAsync(CreateReservationCommand command, CancellationToken cancellationToken)
    {
        await validator.ValidateAndThrowAsync(command, cancellationToken);

        var reservation = Reservation.Create(
            Guid.NewGuid(), command.CustomerName, command.PartySize, command.ReservationDateTime);

        var result = await reservationsData.CreateReservationAsync(reservation, command.OperationId, cancellationToken);

        if (result.WasAlreadyProcessed)
        {
            LogOperationReplayed(command.OperationId, result.ReservationId);
        }
        else
        {
            // CustomerName is deliberately not passed to AuditLogEntry.Details as free text —
            // it's redacted the same way plan.md's retention corrections require for
            // audit/conflict payloads generally, not just this one call site.
            await auditWriter.WriteAsync(
                new AuditLogEntry
                {
                    Id = Guid.NewGuid(),
                    EntityType = "Reservation",
                    EntityId = result.ReservationId,
                    Action = "Created",
                    PerformedByStaffId = command.PerformedByStaffId,
                    OccurredAtUtc = timeProvider.GetUtcNow(),
                    Details = $"Party of {command.PartySize}.",
                },
                cancellationToken);
            LogReservationCreated(result.ReservationId, command.PartySize);
        }

        return result.ReservationId;
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Information,
        Message = "Reservation {ReservationId} created for a party of {PartySize}.")]
    private partial void LogReservationCreated(Guid reservationId, int partySize);

    [LoggerMessage(EventId = 2, Level = LogLevel.Warning,
        Message = "OperationId {OperationId} was already processed; returning existing reservation {ReservationId} instead of creating a new one.")]
    private partial void LogOperationReplayed(Guid operationId, Guid reservationId);
}
