using FluentValidation;
using FluentValidation.Results;
using Mise.Modules.Tables.Contracts;

namespace Mise.Modules.Reservations.Application;

/// <summary>
/// BR-07 ("party size must fit within the assigned table's capacity, or an explicitly
/// combinable set of tables") — shared by CreateReservationCommandHandler and
/// UpdateReservationCommandHandler so the check isn't duplicated. A cross-module,
/// DB-dependent check (via <see cref="ITableAvailabilityLookup"/>, CLAUDE.md's Reservations
/// Phase 6 section / ADR-006), so — same categorization as CreateTableCommandHandler's SectionId
/// check — it throws a field-scoped <see cref="ValidationException"/> (400), matching the
/// charter's own sample contract ("400 → if tableId capacity &lt; partySize ... (BR-07)").
/// </summary>
internal static class TableAssignmentGuard
{
    public static async Task EnsureAssignmentIsValidAsync(
        Guid? tableId, int partySize, ITableAvailabilityLookup tableAvailabilityLookup, CancellationToken cancellationToken)
    {
        if (tableId is not { } id)
        {
            return;
        }

        var capacityInfo = await tableAvailabilityLookup.GetCapacityInfoAsync(id, cancellationToken);
        if (capacityInfo is null)
        {
            throw new ValidationException(
                [new ValidationFailure("TableId", "TableId does not refer to an existing table.")]);
        }

        if (!capacityInfo.IsActive)
        {
            throw new ValidationException([new ValidationFailure("TableId", "TableId refers to an inactive table.")]);
        }

        if (partySize < capacityInfo.CombinedMinCapacity || partySize > capacityInfo.CombinedMaxCapacity)
        {
            throw new ValidationException(
                [new ValidationFailure("PartySize", "PartySize does not fit the assigned table's capacity.")]);
        }
    }
}
