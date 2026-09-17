namespace Mise.Modules.Tables.Application.CreateTable;

public sealed record CreateTableCommand(
    Guid OperationId,
    Guid SectionId,
    string Name,
    int MinCapacity,
    int MaxCapacity,
    bool IsCombinable,
    double? PositionX,
    double? PositionY,
    Guid PerformedByStaffId);
