using Mise.Modules.Scheduling.Domain;

namespace Mise.Modules.Scheduling.Application.Ports;

/// <summary>
/// Slice-specific gateway (CLAUDE.md "The testable seam") — not a generic repository. Every
/// mutation is atomic with the OperationId idempotency check, same shape as
/// ISectionsData.CreateSectionAsync: replaying an already-processed OperationId returns the
/// current ServicePeriod (or, for delete, the already-deleted outcome) and writes nothing new.
/// </summary>
public interface ISchedulingData
{
    Task<ServicePeriodMutationResult> CreateServicePeriodAsync(
        ServicePeriod servicePeriod, Guid operationId, CancellationToken cancellationToken);

    Task<ServicePeriod?> GetServicePeriodByIdAsync(Guid servicePeriodId, CancellationToken cancellationToken);

    Task<IReadOnlyList<ServicePeriod>> GetServicePeriodsForDateAsync(DateOnly date, CancellationToken cancellationToken);

    /// <param name="servicePeriod">Already loaded (via <see cref="GetServicePeriodByIdAsync"/>) and
    /// mutated by the caller via a Domain method — this call persists it.</param>
    Task<ServicePeriodMutationResult> UpdateServicePeriodAsync(
        ServicePeriod servicePeriod, Guid operationId, CancellationToken cancellationToken);

    /// <summary>
    /// Checks the OperationId idempotency table *before* looking the row up by id — a hard
    /// delete, unlike Create/Update, makes the row disappear after the first successful call,
    /// so a replay must not depend on the row still existing to recognise itself as a replay.
    /// <see cref="ServicePeriodDeletionResult.Existed"/> is false only when
    /// <paramref name="servicePeriodId"/> never existed and this OperationId was never recorded
    /// either — a genuine 404, not a replay.
    /// </summary>
    Task<ServicePeriodDeletionResult> DeleteServicePeriodAsync(
        Guid servicePeriodId, Guid operationId, CancellationToken cancellationToken);
}

public sealed record ServicePeriodMutationResult(ServicePeriod ServicePeriod, bool WasAlreadyProcessed);

public sealed record ServicePeriodDeletionResult(bool Existed, bool WasAlreadyProcessed);
