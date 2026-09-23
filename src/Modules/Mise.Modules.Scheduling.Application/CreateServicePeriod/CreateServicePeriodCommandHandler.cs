using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Mise.Modules.Scheduling.Application.Ports;
using Mise.Modules.Scheduling.Domain;
using Mise.SharedKernel.Infrastructure;

namespace Mise.Modules.Scheduling.Application.CreateServicePeriod;

public sealed partial class CreateServicePeriodCommandHandler(
    ISchedulingData schedulingData,
    [FromKeyedServices(AuditWriterKeys.Scheduling)] IAuditWriter auditWriter,
    IValidator<CreateServicePeriodCommand> validator,
    TimeProvider timeProvider,
    ILogger<CreateServicePeriodCommandHandler> logger)
{
    public async Task<Guid> HandleAsync(CreateServicePeriodCommand command, CancellationToken cancellationToken)
    {
        await validator.ValidateAndThrowAsync(command, cancellationToken);

        var servicePeriod = ServicePeriod.Create(
            Guid.NewGuid(), command.Date, command.Label, command.StartTime, command.EndTime,
            command.EndsNextDay, command.IsClosed);

        // Staged before the gateway call, unconditionally (AuditCompletenessInterceptor, Phase
        // 9/ADR-009).
        auditWriter.Stage(new AuditLogEntry
        {
            Id = Guid.NewGuid(),
            EntityType = "ServicePeriod",
            EntityId = servicePeriod.Id,
            Action = "Created",
            PerformedByStaffId = command.PerformedByStaffId,
            OccurredAtUtc = timeProvider.GetUtcNow(),
            Details = $"ServicePeriod '{command.Label}' on {command.Date:yyyy-MM-dd}.",
        });

        var result = await schedulingData.CreateServicePeriodAsync(servicePeriod, command.OperationId, cancellationToken);

        if (result.WasAlreadyProcessed)
        {
            LogOperationReplayed(command.OperationId, result.ServicePeriod.Id);
        }
        else
        {
            LogServicePeriodCreated(result.ServicePeriod.Id);
        }

        return result.ServicePeriod.Id;
    }

    [LoggerMessage(EventId = 70, Level = LogLevel.Information, Message = "ServicePeriod {ServicePeriodId} created.")]
    private partial void LogServicePeriodCreated(Guid servicePeriodId);

    [LoggerMessage(EventId = 71, Level = LogLevel.Warning,
        Message = "OperationId {OperationId} was already processed; returning existing service period {ServicePeriodId} instead of creating a new one.")]
    private partial void LogOperationReplayed(Guid operationId, Guid servicePeriodId);
}
