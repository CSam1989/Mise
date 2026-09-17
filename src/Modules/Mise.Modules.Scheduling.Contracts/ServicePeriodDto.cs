namespace Mise.Modules.Scheduling.Contracts;

public sealed record ServicePeriodDto(
    Guid Id,
    DateOnly Date,
    string Label,
    TimeOnly StartTime,
    TimeOnly EndTime,
    bool EndsNextDay,
    bool IsClosed);
