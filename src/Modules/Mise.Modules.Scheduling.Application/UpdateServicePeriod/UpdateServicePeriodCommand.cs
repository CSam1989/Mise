namespace Mise.Modules.Scheduling.Application.UpdateServicePeriod;

public sealed record UpdateServicePeriodCommand(
    Guid OperationId,
    Guid ServicePeriodId,
    DateOnly Date,
    string Label,
    TimeOnly StartTime,
    TimeOnly EndTime,
    bool EndsNextDay,
    bool IsClosed,
    Guid PerformedByStaffId);
