using FluentValidation;
using FluentValidation.Results;
using Microsoft.Extensions.Logging;
using Mise.Modules.Tables.Application.Ports;
using Mise.Modules.Tables.Domain;
using Mise.SharedKernel.Infrastructure;

namespace Mise.Modules.Tables.Application.CreateTable;

public sealed partial class CreateTableCommandHandler(
    ITablesData tablesData,
    ISectionsData sectionsData,
    IAuditWriter auditWriter,
    IValidator<CreateTableCommand> validator,
    TimeProvider timeProvider,
    ILogger<CreateTableCommandHandler> logger)
{
    /// <summary>Returns the gateway's own <see cref="TableCreateResult"/> (not just the new id,
    /// unlike CreateReservationCommandHandler) because the endpoint needs the freshly assigned
    /// xmin version to set the response's ETag header — the first mutating response in this
    /// codebase that has to (docs/plan.md correction #5).</summary>
    public async Task<TableCreateResult> HandleAsync(CreateTableCommand command, CancellationToken cancellationToken)
    {
        await validator.ValidateAndThrowAsync(command, cancellationToken);

        // A non-existent SectionId is an expected, field-scoped outcome (same shape as
        // RegisterStaffCommandHandler's taken-username check) — without this, it would surface
        // as a raw foreign-key-violation 500 from Postgres instead, since the DB-level FK
        // (added alongside the unique (SectionId, Name) index) is the only other thing that
        // would catch it.
        if (await sectionsData.GetSectionByIdAsync(command.SectionId, cancellationToken) is null)
        {
            throw new ValidationException(
                [new ValidationFailure(nameof(command.SectionId), "SectionId does not refer to an existing section.")]);
        }

        var table = Table.Create(
            Guid.NewGuid(), command.SectionId, command.Name, command.MinCapacity, command.MaxCapacity,
            command.IsCombinable, command.PositionX, command.PositionY);

        var result = await tablesData.CreateTableAsync(table, command.OperationId, cancellationToken);

        if (result.WasAlreadyProcessed)
        {
            LogOperationReplayed(command.OperationId, result.TableId);
        }
        else
        {
            await auditWriter.WriteAsync(
                new AuditLogEntry
                {
                    Id = Guid.NewGuid(),
                    EntityType = "Table",
                    EntityId = result.TableId,
                    Action = "Created",
                    PerformedByStaffId = command.PerformedByStaffId,
                    OccurredAtUtc = timeProvider.GetUtcNow(),
                    Details = $"Table '{command.Name}' in section {command.SectionId}.",
                },
                cancellationToken);
            LogTableCreated(result.TableId);
        }

        return result;
    }

    [LoggerMessage(EventId = 50, Level = LogLevel.Information, Message = "Table {TableId} created.")]
    private partial void LogTableCreated(Guid tableId);

    [LoggerMessage(EventId = 51, Level = LogLevel.Warning,
        Message = "OperationId {OperationId} was already processed; returning existing table {TableId} instead of creating a new one.")]
    private partial void LogOperationReplayed(Guid operationId, Guid tableId);
}
