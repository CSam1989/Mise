namespace Mise.Modules.Scheduling.Application.CreateServicePeriod;

public sealed record CreateServicePeriodCommand(
    Guid OperationId,
    DateOnly Date,
    string Label,
    TimeOnly StartTime,
    TimeOnly EndTime,
    bool EndsNextDay,
    bool IsClosed,
    Guid PerformedByStaffId);
