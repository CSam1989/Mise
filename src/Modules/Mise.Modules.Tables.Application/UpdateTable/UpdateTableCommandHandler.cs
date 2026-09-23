using FluentValidation;
using FluentValidation.Results;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Mise.Modules.Tables.Application.Ports;
using Mise.Modules.Tables.Domain;
using Mise.SharedKernel;
using Mise.SharedKernel.Infrastructure;

namespace Mise.Modules.Tables.Application.UpdateTable;

/// <summary>
/// Returns null for a missing <see cref="UpdateTableCommand.TableId"/> (see
/// UpdateSectionCommandHandler's doc comment for why that's a plain null rather than a new
/// exception type). A stale <see cref="UpdateTableCommand.ExpectedVersion"/> throws
/// <see cref="ConcurrencyConflictException"/> — docs/plan.md correction #5's 409 path —
/// caught by Mise.ApiService's ConcurrencyConflictExceptionHandler.
/// </summary>
public sealed partial class UpdateTableCommandHandler(
    ITablesData tablesData,
    ISectionsData sectionsData,
    [FromKeyedServices(AuditWriterKeys.Tables)] IAuditWriter auditWriter,
    IValidator<UpdateTableCommand> validator,
    TimeProvider timeProvider,
    ILogger<UpdateTableCommandHandler> logger)
{
    public async Task<TableSaveResult?> HandleAsync(UpdateTableCommand command, CancellationToken cancellationToken)
    {
        await validator.ValidateAndThrowAsync(command, cancellationToken);

        var current = await tablesData.GetTableByIdAsync(command.TableId, cancellationToken);
        if (current is null)
        {
            return null;
        }

        // See CreateTableCommandHandler's identical check for why this can't be left to the
        // DB-level FK alone.
        if (await sectionsData.GetSectionByIdAsync(command.SectionId, cancellationToken) is null)
        {
            throw new ValidationException(
                [new ValidationFailure(nameof(command.SectionId), "SectionId does not refer to an existing section.")]);
        }

        current.Table.UpdateDetails(
            command.SectionId, command.Name, command.MinCapacity, command.MaxCapacity,
            command.IsCombinable, command.PositionX, command.PositionY);

        // Staged before the gateway call, unconditionally (AuditCompletenessInterceptor, Phase
        // 9/ADR-009).
        auditWriter.Stage(new AuditLogEntry
        {
            Id = Guid.NewGuid(),
            EntityType = "Table",
            EntityId = command.TableId,
            Action = "Updated",
            PerformedByStaffId = command.PerformedByStaffId,
            OccurredAtUtc = timeProvider.GetUtcNow(),
            Details = $"Table '{command.Name}'.",
        });

        var result = await tablesData.UpdateTableAsync(
            current.Table, command.ExpectedVersion, command.OperationId, cancellationToken);

        if (result.Outcome == TableSaveOutcome.VersionMismatch)
        {
            LogVersionMismatch(command.TableId, command.ExpectedVersion, result.Version);
            throw new ConcurrencyConflictException(
                "Table", command.TableId, result.Version, CurrentStateOf(result.Table));
        }

        if (result.WasAlreadyProcessed)
        {
            LogOperationReplayed(command.OperationId, command.TableId);
        }
        else
        {
            LogTableUpdated(command.TableId);
        }

        return result;
    }

    private static Dictionary<string, object?> CurrentStateOf(Table table) => new()
    {
        ["sectionId"] = table.SectionId,
        ["name"] = table.Name,
        ["minCapacity"] = table.MinCapacity,
        ["maxCapacity"] = table.MaxCapacity,
        ["isCombinable"] = table.IsCombinable,
        ["positionX"] = table.PositionX,
        ["positionY"] = table.PositionY,
        ["status"] = table.Status.ToString(),
    };

    [LoggerMessage(EventId = 52, Level = LogLevel.Information, Message = "Table {TableId} updated.")]
    private partial void LogTableUpdated(Guid tableId);

    [LoggerMessage(EventId = 53, Level = LogLevel.Warning,
        Message = "OperationId {OperationId} was already processed for table {TableId}; skipping the update.")]
    private partial void LogOperationReplayed(Guid operationId, Guid tableId);

    [LoggerMessage(EventId = 54, Level = LogLevel.Warning,
        Message = "Table {TableId} update rejected: caller's version {ExpectedVersion} does not match current version {CurrentVersion}.")]
    private partial void LogVersionMismatch(Guid tableId, uint expectedVersion, uint currentVersion);
}
