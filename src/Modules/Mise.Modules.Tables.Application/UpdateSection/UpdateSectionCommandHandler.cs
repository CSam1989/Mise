using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Mise.Modules.Tables.Application.Ports;
using Mise.SharedKernel.Infrastructure;

namespace Mise.Modules.Tables.Application.UpdateSection;

/// <summary>Returns null when <see cref="UpdateSectionCommand.SectionId"/> doesn't exist —
/// there is no established NotFoundException pattern in this codebase yet (Phase 2/3 never
/// had a lookup-by-id that could miss), so the endpoint maps null to 404 itself rather than a
/// new exception type being introduced for a single caller.</summary>
public sealed partial class UpdateSectionCommandHandler(
    ISectionsData sectionsData,
    [FromKeyedServices(AuditWriterKeys.Tables)] IAuditWriter auditWriter,
    IValidator<UpdateSectionCommand> validator,
    TimeProvider timeProvider,
    ILogger<UpdateSectionCommandHandler> logger)
{
    public async Task<SectionMutationResult?> HandleAsync(UpdateSectionCommand command, CancellationToken cancellationToken)
    {
        await validator.ValidateAndThrowAsync(command, cancellationToken);

        var section = await sectionsData.GetSectionByIdAsync(command.SectionId, cancellationToken);
        if (section is null)
        {
            return null;
        }

        section.UpdateDetails(command.Name, command.DisplayOrder);

        // Staged before the gateway call, unconditionally (AuditCompletenessInterceptor, Phase
        // 9/ADR-009).
        auditWriter.Stage(new AuditLogEntry
        {
            Id = Guid.NewGuid(),
            EntityType = "Section",
            EntityId = command.SectionId,
            Action = "Updated",
            PerformedByStaffId = command.PerformedByStaffId,
            OccurredAtUtc = timeProvider.GetUtcNow(),
            Details = $"Section '{command.Name}'.",
        });

        var result = await sectionsData.UpdateSectionAsync(section, command.OperationId, cancellationToken);

        if (result.WasAlreadyProcessed)
        {
            LogOperationReplayed(command.OperationId, command.SectionId);
        }
        else
        {
            LogSectionUpdated(command.SectionId);
        }

        return result;
    }

    [LoggerMessage(EventId = 42, Level = LogLevel.Information, Message = "Section {SectionId} updated.")]
    private partial void LogSectionUpdated(Guid sectionId);

    [LoggerMessage(EventId = 43, Level = LogLevel.Warning,
        Message = "OperationId {OperationId} was already processed for section {SectionId}; skipping the update.")]
    private partial void LogOperationReplayed(Guid operationId, Guid sectionId);
}
