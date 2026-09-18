namespace Mise.ApiService.Tables;

internal sealed record CreateTableRequest(
    Guid OperationId,
    Guid SectionId,
    string Name,
    int MinCapacity,
    int MaxCapacity,
    bool IsCombinable,
    double? PositionX,
    double? PositionY);

internal sealed record UpdateTableRequest(
    Guid OperationId,
    Guid SectionId,
    string Name,
    int MinCapacity,
    int MaxCapacity,
    bool IsCombinable,
    double? PositionX,
    double? PositionY);

internal sealed record DeactivateTableRequest(Guid OperationId);

internal sealed record ChangeTableStatusRequest(Guid OperationId, string Status);
