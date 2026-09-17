namespace Mise.Modules.Tables.Application.UpdateTable;

public sealed record UpdateTableCommand(
    Guid OperationId,
    Guid TableId,
    uint ExpectedVersion,
    Guid SectionId,
    string Name,
    int MinCapacity,
    int MaxCapacity,
    bool IsCombinable,
    double? PositionX,
    double? PositionY,
    Guid PerformedByStaffId);
