using Mise.Modules.Tables.Domain;

namespace Mise.Modules.Tables.Application.Ports;

/// <summary>
/// Slice-specific gateway (CLAUDE.md "The testable seam") — not a generic repository. Every
/// mutation is atomic with the OperationId idempotency check, same shape as
/// IReservationsData.CreateReservationAsync: replaying an already-processed OperationId
/// returns the current Section and writes nothing new.
/// </summary>
public interface ISectionsData
{
    Task<SectionMutationResult> CreateSectionAsync(Section section, Guid operationId, CancellationToken cancellationToken);

    Task<Section?> GetSectionByIdAsync(Guid sectionId, CancellationToken cancellationToken);

    Task<IReadOnlyList<Section>> GetActiveSectionsAsync(CancellationToken cancellationToken);

    /// <param name="section">Already loaded (via <see cref="GetSectionByIdAsync"/>) and mutated
    /// by the caller via a Domain method — this call persists it.</param>
    Task<SectionMutationResult> UpdateSectionAsync(Section section, Guid operationId, CancellationToken cancellationToken);

    Task<SectionMutationResult> DeactivateSectionAsync(Section section, Guid operationId, CancellationToken cancellationToken);
}

public sealed record SectionMutationResult(Section Section, bool WasAlreadyProcessed);
