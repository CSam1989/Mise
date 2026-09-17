using Microsoft.Extensions.Logging;
using Mise.Modules.Scheduling.Application.Ports;
using Mise.SharedKernel.Infrastructure;

namespace Mise.Modules.Scheduling.Application.DeleteServicePeriod;

/// <summary>
/// No FluentValidation validator: beyond the id (already Guid-typed by model binding), there
/// is no field-shape rule to check — same reasoning as DeactivateSectionCommandHandler. Unlike
/// Section/Table, ServicePeriod has no IsActive column in the charter's data model, so removing
/// one is a real delete rather than a soft-deactivate. No Domain method call either: deletion
/// has no aggregate-level invariant to enforce, so this handler never loads a ServicePeriod at
/// all — it delegates the whole existence-and-idempotency decision to the gateway (see
/// ISchedulingData.DeleteServicePeriodAsync's remarks on why the OperationId check must run
/// before the existence check for a hard delete).
/// </summary>
public sealed partial class DeleteServicePeriodCommandHandler(
    ISchedulingData schedulingData,
    IAuditWriter auditWriter,
    TimeProvider timeProvider,
    ILogger<DeleteServicePeriodCommandHandler> logger)
{
    public async Task<bool?> HandleAsync(DeleteServicePeriodCommand command, CancellationToken cancellationToken)
    {
        var result = await schedulingData.DeleteServicePeriodAsync(command.ServicePeriodId, command.OperationId, cancellationToken);
        if (!result.Existed)
        {
            return null;
        }

        if (result.WasAlreadyProcessed)
        {
            LogOperationReplayed(command.OperationId, command.ServicePeriodId);
        }
        else
        {
            await auditWriter.WriteAsync(
                new AuditLogEntry
                {
                    Id = Guid.NewGuid(),
                    EntityType = "ServicePeriod",
                    EntityId = command.ServicePeriodId,
                    Action = "Deleted",
                    PerformedByStaffId = command.PerformedByStaffId,
                    OccurredAtUtc = timeProvider.GetUtcNow(),
                    Details = string.Empty,
                },
                cancellationToken);
            LogServicePeriodDeleted(command.ServicePeriodId);
        }

        return true;
    }

    [LoggerMessage(EventId = 74, Level = LogLevel.Information, Message = "ServicePeriod {ServicePeriodId} deleted.")]
    private partial void LogServicePeriodDeleted(Guid servicePeriodId);

    [LoggerMessage(EventId = 75, Level = LogLevel.Warning,
        Message = "OperationId {OperationId} was already processed for service period {ServicePeriodId}; skipping the delete.")]
    private partial void LogOperationReplayed(Guid operationId, Guid servicePeriodId);
}
