using Microsoft.Extensions.Logging;
using Mise.Modules.Tables.Application.Ports;
using Mise.Modules.Tables.Domain;
using Mise.SharedKernel;
using Mise.SharedKernel.Infrastructure;

namespace Mise.Modules.Tables.Application.DeactivateTable;

/// <summary>
/// No FluentValidation validator — see DeactivateSectionCommandHandler's doc comment; the
/// same reasoning applies here. <see cref="Table.Deactivate"/> itself throws
/// <see cref="DomainRuleViolationException"/> for the Occupied/Reserved guard (docs/plan.md
/// correction #13) — a self-contained Domain invariant, unlike Section's cross-aggregate
/// "has active tables" check, which stays at the handler level.
/// </summary>
public sealed partial class DeactivateTableCommandHandler(
    ITablesData tablesData,
    IAuditWriter auditWriter,
    TimeProvider timeProvider,
    ILogger<DeactivateTableCommandHandler> logger)
{
    public async Task<TableSaveResult?> HandleAsync(DeactivateTableCommand command, CancellationToken cancellationToken)
    {
        var current = await tablesData.GetTableByIdAsync(command.TableId, cancellationToken);
        if (current is null)
        {
            return null;
        }

        current.Table.Deactivate();

        var result = await tablesData.DeactivateTableAsync(
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
            await auditWriter.WriteAsync(
                new AuditLogEntry
                {
                    Id = Guid.NewGuid(),
                    EntityType = "Table",
                    EntityId = command.TableId,
                    Action = "Deactivated",
                    PerformedByStaffId = command.PerformedByStaffId,
                    OccurredAtUtc = timeProvider.GetUtcNow(),
                    Details = string.Empty,
                },
                cancellationToken);
            LogTableDeactivated(command.TableId);
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

    [LoggerMessage(EventId = 55, Level = LogLevel.Information, Message = "Table {TableId} deactivated.")]
    private partial void LogTableDeactivated(Guid tableId);

    [LoggerMessage(EventId = 56, Level = LogLevel.Warning,
        Message = "OperationId {OperationId} was already processed for table {TableId}; skipping the deactivation.")]
    private partial void LogOperationReplayed(Guid operationId, Guid tableId);

    [LoggerMessage(EventId = 57, Level = LogLevel.Warning,
        Message = "Table {TableId} deactivate rejected: caller's version {ExpectedVersion} does not match current version {CurrentVersion}.")]
    private partial void LogVersionMismatch(Guid tableId, uint expectedVersion, uint currentVersion);
}
