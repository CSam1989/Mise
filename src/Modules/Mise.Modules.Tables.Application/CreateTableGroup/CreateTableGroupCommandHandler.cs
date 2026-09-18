using FluentValidation;
using FluentValidation.Results;
using Microsoft.Extensions.Logging;
using Mise.Modules.Tables.Application.Ports;
using Mise.Modules.Tables.Domain;
using Mise.SharedKernel.Infrastructure;

namespace Mise.Modules.Tables.Application.CreateTableGroup;

/// <summary>
/// Every member table must exist, be active, and be marked <see cref="Table.IsCombinable"/> —
/// and must not already belong to another active group (BR-07's "explicitly combinable set"
/// is unambiguous only if a table combines with exactly one set at a time). All three are
/// cross-aggregate, DB-dependent checks, so — same categorization as
/// CreateTableCommandHandler's SectionId check — they throw a field-scoped
/// <see cref="ValidationException"/> (400) here in Application, not a Domain invariant.
/// <see cref="ITableGroupsData.FindExistingTableGroupIdAsync"/> is checked *first*, before any
/// of that validation runs — a replay of an already-processed OperationId would otherwise fail
/// the "not already grouped" check for exactly the tables its own first run just grouped (same
/// pitfall, same fix, as CLAUDE.md's Scheduling section already documents for hard deletes).
/// </summary>
public sealed partial class CreateTableGroupCommandHandler(
    ITableGroupsData tableGroupsData,
    ITablesData tablesData,
    IAuditWriter auditWriter,
    IValidator<CreateTableGroupCommand> validator,
    TimeProvider timeProvider,
    ILogger<CreateTableGroupCommandHandler> logger)
{
    public async Task<TableGroupCreateResult> HandleAsync(CreateTableGroupCommand command, CancellationToken cancellationToken)
    {
        await validator.ValidateAndThrowAsync(command, cancellationToken);

        var existingId = await tableGroupsData.FindExistingTableGroupIdAsync(command.OperationId, cancellationToken);
        if (existingId is { } id)
        {
            LogOperationReplayed(command.OperationId, id);
            return new TableGroupCreateResult(id, WasAlreadyProcessed: true);
        }

        var distinctTableIds = command.TableIds.Distinct().ToArray();

        foreach (var tableId in distinctTableIds)
        {
            var table = await tablesData.GetTableByIdAsync(tableId, cancellationToken);
            if (table is null || !table.Table.IsActive)
            {
                throw new ValidationException(
                    [new ValidationFailure(nameof(command.TableIds), $"Table '{tableId}' does not refer to an existing, active table.")]);
            }

            if (!table.Table.IsCombinable)
            {
                throw new ValidationException(
                    [new ValidationFailure(nameof(command.TableIds), $"Table '{tableId}' is not marked combinable.")]);
            }
        }

        var alreadyGrouped = await tableGroupsData.GetTableIdsAlreadyInAnActiveGroupAsync(distinctTableIds, cancellationToken);
        if (alreadyGrouped.Count > 0)
        {
            throw new ValidationException(
                [new ValidationFailure(nameof(command.TableIds), $"Table '{alreadyGrouped[0]}' already belongs to another active table group.")]);
        }

        var tableGroup = TableGroup.Create(Guid.NewGuid(), command.Name, distinctTableIds);

        var result = await tableGroupsData.CreateTableGroupAsync(tableGroup, command.OperationId, cancellationToken);

        if (result.WasAlreadyProcessed)
        {
            LogOperationReplayed(command.OperationId, result.TableGroupId);
        }
        else
        {
            await auditWriter.WriteAsync(
                new AuditLogEntry
                {
                    Id = Guid.NewGuid(),
                    EntityType = "TableGroup",
                    EntityId = result.TableGroupId,
                    Action = "Created",
                    PerformedByStaffId = command.PerformedByStaffId,
                    OccurredAtUtc = timeProvider.GetUtcNow(),
                    Details = $"TableGroup '{command.Name}' with {distinctTableIds.Length} tables.",
                },
                cancellationToken);
            LogTableGroupCreated(result.TableGroupId);
        }

        return result;
    }

    [LoggerMessage(EventId = 70, Level = LogLevel.Information, Message = "TableGroup {TableGroupId} created.")]
    private partial void LogTableGroupCreated(Guid tableGroupId);

    [LoggerMessage(EventId = 71, Level = LogLevel.Warning,
        Message = "OperationId {OperationId} was already processed; returning existing table group {TableGroupId} instead of creating a new one.")]
    private partial void LogOperationReplayed(Guid operationId, Guid tableGroupId);
}
