namespace Mise.Modules.Tables.Application.DeactivateTable;

public sealed record DeactivateTableCommand(Guid OperationId, Guid TableId, uint ExpectedVersion, Guid PerformedByStaffId);
