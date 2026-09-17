namespace Mise.Modules.Scheduling.Application.DeleteServicePeriod;

public sealed record DeleteServicePeriodCommand(Guid OperationId, Guid ServicePeriodId, Guid PerformedByStaffId);
