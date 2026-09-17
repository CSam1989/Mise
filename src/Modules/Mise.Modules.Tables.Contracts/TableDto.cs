namespace Mise.Modules.Tables.Contracts;

/// <summary>
/// <see cref="Version"/> is the caller-facing form of the Postgres <c>xmin</c> optimistic
/// concurrency token (docs/plan.md correction #5) — round-tripped as an <c>If-Match</c> header
/// on the next PATCH, not just informational. Included on every response (not only the ETag
/// header) so a floor-plan list response lets a client act on any one row without a separate
/// GET per table.
/// </summary>
public sealed record TableDto(
    Guid Id,
    Guid SectionId,
    string Name,
    int MinCapacity,
    int MaxCapacity,
    bool IsCombinable,
    double? PositionX,
    double? PositionY,
    string Status,
    bool IsActive,
    uint Version);
