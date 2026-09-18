using FluentValidation;
using Microsoft.Extensions.Logging;
using Mise.Modules.Tables.Application.Ports;
using Mise.Modules.Tables.Domain;
using Mise.SharedKernel;
using Mise.SharedKernel.Infrastructure;

namespace Mise.Modules.Tables.Application.ChangeTableStatus;

/// <summary>
/// FR-06 — <see cref="Table.SetStatus"/> is an unconditional staff override (no Domain-level
/// transition guard; see that method's doc comment), so unlike DeactivateTableCommandHandler
/// this never throws <see cref="DomainRuleViolationException"/>. Reuses
/// <see cref="ITablesData.ChangeTableStatusAsync"/>, a distinctly-named gateway method whose
/// implementation is identical to <see cref="ITablesData.UpdateTableAsync"/>'s — same
/// per-verb-naming convention <see cref="ITablesData.DeactivateTableAsync"/> already
/// established, kept for audit/log clarity rather than DRY-ing the interface down to one method.
/// </summary>
public sealed partial class ChangeTableStatusCommandHandler(
    ITablesData tablesData,
    IAuditWriter auditWriter,
    IValidator<ChangeTableStatusCommand> validator,
    TimeProvider timeProvider,
    ILogger<ChangeTableStatusCommandHandler> logger)
{
    public async Task<TableSaveResult?> HandleAsync(ChangeTableStatusCommand command, CancellationToken cancellationToken)
    {
        await validator.ValidateAndThrowAsync(command, cancellationToken);

        var current = await tablesData.GetTableByIdAsync(command.TableId, cancellationToken);
        if (current is null)
        {
            return null;
        }

        var newStatus = Enum.Parse<TableStatus>(command.Status);
        current.Table.SetStatus(newStatus);

        var result = await tablesData.ChangeTableStatusAsync(
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
                    Action = "StatusChanged",
                    PerformedByStaffId = command.PerformedByStaffId,
                    OccurredAtUtc = timeProvider.GetUtcNow(),
                    Details = newStatus.ToString(),
                },
                cancellationToken);
            LogTableStatusChanged(command.TableId, newStatus);
        }

        return result;
    }

    private static Dictionary<string, object?> CurrentStateOf(Table table) => new()
    {
        ["sectionId"] = table.SectionId,
        ["name"] = table.Name,
        ["status"] = table.Status.ToString(),
    };

    [LoggerMessage(EventId = 58, Level = LogLevel.Information, Message = "Table {TableId} status changed to {NewStatus}.")]
    private partial void LogTableStatusChanged(Guid tableId, TableStatus newStatus);

    [LoggerMessage(EventId = 59, Level = LogLevel.Warning,
        Message = "OperationId {OperationId} was already processed for table {TableId}; skipping the status-change audit write.")]
    private partial void LogOperationReplayed(Guid operationId, Guid tableId);

    [LoggerMessage(EventId = 60, Level = LogLevel.Warning,
        Message = "Table {TableId} status-change rejected: caller's version {ExpectedVersion} does not match current version {CurrentVersion}.")]
    private partial void LogVersionMismatch(Guid tableId, uint expectedVersion, uint currentVersion);
}
