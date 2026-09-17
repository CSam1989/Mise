namespace Mise.ApiService.Scheduling;

internal sealed record CreateServicePeriodRequest(
    Guid OperationId, DateOnly Date, string Label, TimeOnly StartTime, TimeOnly EndTime, bool EndsNextDay, bool IsClosed);

internal sealed record UpdateServicePeriodRequest(
    Guid OperationId, DateOnly Date, string Label, TimeOnly StartTime, TimeOnly EndTime, bool EndsNextDay, bool IsClosed);
