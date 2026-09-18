namespace Mise.Modules.Tables.Application.CreateTableGroup;

public sealed record CreateTableGroupCommand(
    Guid OperationId,
    string Name,
    IReadOnlyList<Guid> TableIds,
    Guid PerformedByStaffId);
