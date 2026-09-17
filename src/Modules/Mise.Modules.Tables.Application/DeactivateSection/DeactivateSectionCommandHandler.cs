using FluentValidation;
using FluentValidation.Results;
using Microsoft.Extensions.Logging;
using Mise.Modules.Tables.Application.Ports;
using Mise.SharedKernel.Infrastructure;

namespace Mise.Modules.Tables.Application.DeactivateSection;

/// <summary>
/// No FluentValidation validator: beyond the two ids (already Guid-typed by model binding),
/// there is no field-shape rule to check — unlike Create/Update, which validate Name/
/// DisplayOrder. The "still has active tables" guard is deliberately not a Domain invariant
/// on Section (docs/plan.md correction #12): it needs a query across Table rows, which the
/// aggregate has no way to run, so it's routed through the same field-scoped ValidationException
/// shape RegisterStaffCommandHandler's taken-username check uses — "is this a layering leak?"
/// was already litigated there in Phase 3's checklist and answered no.
/// </summary>
public sealed partial class DeactivateSectionCommandHandler(
    ISectionsData sectionsData,
    ITablesData tablesData,
    IAuditWriter auditWriter,
    TimeProvider timeProvider,
    ILogger<DeactivateSectionCommandHandler> logger)
{
    public async Task<SectionMutationResult?> HandleAsync(DeactivateSectionCommand command, CancellationToken cancellationToken)
    {
        var section = await sectionsData.GetSectionByIdAsync(command.SectionId, cancellationToken);
        if (section is null)
        {
            return null;
        }

        if (await tablesData.AnyActiveTablesInSectionAsync(command.SectionId, cancellationToken))
        {
            throw new ValidationException(
                [new ValidationFailure(nameof(command.SectionId), "Cannot deactivate a section that still has active tables.")]);
        }

        section.Deactivate();

        var result = await sectionsData.DeactivateSectionAsync(section, command.OperationId, cancellationToken);

        if (result.WasAlreadyProcessed)
        {
            LogOperationReplayed(command.OperationId, command.SectionId);
        }
        else
        {
            await auditWriter.WriteAsync(
                new AuditLogEntry
                {
                    Id = Guid.NewGuid(),
                    EntityType = "Section",
                    EntityId = command.SectionId,
                    Action = "Deactivated",
                    PerformedByStaffId = command.PerformedByStaffId,
                    OccurredAtUtc = timeProvider.GetUtcNow(),
                    Details = string.Empty,
                },
                cancellationToken);
            LogSectionDeactivated(command.SectionId);
        }

        return result;
    }

    [LoggerMessage(EventId = 44, Level = LogLevel.Information, Message = "Section {SectionId} deactivated.")]
    private partial void LogSectionDeactivated(Guid sectionId);

    [LoggerMessage(EventId = 45, Level = LogLevel.Warning,
        Message = "OperationId {OperationId} was already processed for section {SectionId}; skipping the deactivation.")]
    private partial void LogOperationReplayed(Guid operationId, Guid sectionId);
}
