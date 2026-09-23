using FluentValidation;
using FluentValidation.Results;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Mise.Modules.StaffIdentity.Application.Ports;
using Mise.Modules.StaffIdentity.Domain;
using Mise.SharedKernel.Infrastructure;

namespace Mise.Modules.StaffIdentity.Application.RegisterStaff;

/// <summary>
/// Mirrors CreateReservationCommandHandler's shape (CLAUDE.md's handler test contract):
/// validates first, throws FluentValidation's own ValidationException on invalid input, and
/// never touches the gateway or audit writer in that case. A taken username is treated the
/// same way — an expected, field-scoped 400 — even though the gateway (not the validator)
/// is what actually detects it, since only a real unique-constraint check can catch a race.
/// </summary>
public sealed partial class RegisterStaffCommandHandler(
    IStaffIdentityData staffIdentityData,
    [FromKeyedServices(AuditWriterKeys.StaffIdentity)] IAuditWriter auditWriter,
    IValidator<RegisterStaffCommand> validator,
    TimeProvider timeProvider,
    ILogger<RegisterStaffCommandHandler> logger)
{
    public async Task<Guid> HandleAsync(RegisterStaffCommand command, CancellationToken cancellationToken)
    {
        await validator.ValidateAndThrowAsync(command, cancellationToken);

        var staffUser = StaffUser.Create(Guid.NewGuid(), command.FullName, command.Role);

        // Staged before the gateway call, unconditionally (AuditCompletenessInterceptor, Phase
        // 9/ADR-009). On UsernameTaken or an idempotent replay, RegisterStaffAsync never reaches
        // its own SaveChangesAsync (see StaffIdentityData's remarks on AutoSaveChanges=false), so
        // this staged-but-unflushed entry is simply discarded with the DbContext.
        auditWriter.Stage(new AuditLogEntry
        {
            Id = Guid.NewGuid(),
            EntityType = "StaffUser",
            EntityId = staffUser.Id,
            Action = "Created",
            PerformedByStaffId = command.PerformedByStaffId,
            OccurredAtUtc = timeProvider.GetUtcNow(),
            Details = $"Role {command.Role}.",
        });

        var result = await staffIdentityData.RegisterStaffAsync(
            staffUser, command.Username, command.Password, command.OperationId, cancellationToken);

        switch (result.Outcome)
        {
            case RegisterStaffOutcome.UsernameTaken:
                LogUsernameTaken();
                throw new ValidationException(
                    [new ValidationFailure(nameof(command.Username), "This username is already taken.")]);

            case RegisterStaffOutcome.AlreadyProcessed:
                LogOperationReplayed(command.OperationId, result.StaffUserId);
                return result.StaffUserId;

            default:
                LogStaffRegistered(result.StaffUserId, command.Role);
                return result.StaffUserId;
        }
    }

    [LoggerMessage(EventId = 50, Level = LogLevel.Information,
        Message = "Staff user {StaffUserId} registered with role {Role}.")]
    private partial void LogStaffRegistered(Guid staffUserId, StaffRole role);

    [LoggerMessage(EventId = 51, Level = LogLevel.Warning,
        Message = "OperationId {OperationId} was already processed; returning existing staff user {StaffUserId} instead of creating a new one.")]
    private partial void LogOperationReplayed(Guid operationId, Guid staffUserId);

    [LoggerMessage(EventId = 52, Level = LogLevel.Debug,
        Message = "Registration rejected: the requested username is already taken.")]
    private partial void LogUsernameTaken();
}
