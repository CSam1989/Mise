using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Mise.Modules.Scheduling.Application.Ports;
using Mise.SharedKernel.Infrastructure;

namespace Mise.Modules.Scheduling.Application.UpdateServicePeriod;

/// <summary>Returns null when <see cref="UpdateServicePeriodCommand.ServicePeriodId"/> doesn't
/// exist — same shape as UpdateSectionCommandHandler: the endpoint maps null to 404 itself
/// rather than a new NotFoundException type for a single caller.</summary>
public sealed partial class UpdateServicePeriodCommandHandler(
    ISchedulingData schedulingData,
    [FromKeyedServices(AuditWriterKeys.Scheduling)] IAuditWriter auditWriter,
    IValidator<UpdateServicePeriodCommand> validator,
    TimeProvider timeProvider,
    ILogger<UpdateServicePeriodCommandHandler> logger)
{
    public async Task<ServicePeriodMutationResult?> HandleAsync(
        UpdateServicePeriodCommand command, CancellationToken cancellationToken)
    {
        await validator.ValidateAndThrowAsync(command, cancellationToken);

        var servicePeriod = await schedulingData.GetServicePeriodByIdAsync(command.ServicePeriodId, cancellationToken);
        if (servicePeriod is null)
        {
            return null;
        }

        servicePeriod.UpdateDetails(
            command.Date, command.Label, command.StartTime, command.EndTime, command.EndsNextDay, command.IsClosed);

        // Staged before the gateway call, unconditionally (AuditCompletenessInterceptor, Phase
        // 9/ADR-009).
        auditWriter.Stage(new AuditLogEntry
        {
            Id = Guid.NewGuid(),
            EntityType = "ServicePeriod",
            EntityId = command.ServicePeriodId,
            Action = "Updated",
            PerformedByStaffId = command.PerformedByStaffId,
            OccurredAtUtc = timeProvider.GetUtcNow(),
            Details = $"ServicePeriod '{command.Label}' on {command.Date:yyyy-MM-dd}.",
        });

        var result = await schedulingData.UpdateServicePeriodAsync(servicePeriod, command.OperationId, cancellationToken);

        if (result.WasAlreadyProcessed)
        {
            LogOperationReplayed(command.OperationId, command.ServicePeriodId);
        }
        else
        {
            LogServicePeriodUpdated(command.ServicePeriodId);
        }

        return result;
    }

    [LoggerMessage(EventId = 72, Level = LogLevel.Information, Message = "ServicePeriod {ServicePeriodId} updated.")]
    private partial void LogServicePeriodUpdated(Guid servicePeriodId);

    [LoggerMessage(EventId = 73, Level = LogLevel.Warning,
        Message = "OperationId {OperationId} was already processed for service period {ServicePeriodId}; skipping the update.")]
    private partial void LogOperationReplayed(Guid operationId, Guid servicePeriodId);
}
