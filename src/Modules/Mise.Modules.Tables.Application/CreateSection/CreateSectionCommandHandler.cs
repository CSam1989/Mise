using FluentValidation;
using Microsoft.Extensions.Logging;
using Mise.Modules.Tables.Application.Ports;
using Mise.Modules.Tables.Domain;
using Mise.SharedKernel.Infrastructure;

namespace Mise.Modules.Tables.Application.CreateSection;

public sealed partial class CreateSectionCommandHandler(
    ISectionsData sectionsData,
    IAuditWriter auditWriter,
    IValidator<CreateSectionCommand> validator,
    TimeProvider timeProvider,
    ILogger<CreateSectionCommandHandler> logger)
{
    public async Task<Guid> HandleAsync(CreateSectionCommand command, CancellationToken cancellationToken)
    {
        await validator.ValidateAndThrowAsync(command, cancellationToken);

        var section = Section.Create(Guid.NewGuid(), command.Name, command.DisplayOrder);

        var result = await sectionsData.CreateSectionAsync(section, command.OperationId, cancellationToken);

        if (result.WasAlreadyProcessed)
        {
            LogOperationReplayed(command.OperationId, result.Section.Id);
        }
        else
        {
            await auditWriter.WriteAsync(
                new AuditLogEntry
                {
                    Id = Guid.NewGuid(),
                    EntityType = "Section",
                    EntityId = result.Section.Id,
                    Action = "Created",
                    PerformedByStaffId = command.PerformedByStaffId,
                    OccurredAtUtc = timeProvider.GetUtcNow(),
                    Details = $"Section '{command.Name}'.",
                },
                cancellationToken);
            LogSectionCreated(result.Section.Id);
        }

        return result.Section.Id;
    }

    [LoggerMessage(EventId = 40, Level = LogLevel.Information, Message = "Section {SectionId} created.")]
    private partial void LogSectionCreated(Guid sectionId);

    [LoggerMessage(EventId = 41, Level = LogLevel.Warning,
        Message = "OperationId {OperationId} was already processed; returning existing section {SectionId} instead of creating a new one.")]
    private partial void LogOperationReplayed(Guid operationId, Guid sectionId);
}
