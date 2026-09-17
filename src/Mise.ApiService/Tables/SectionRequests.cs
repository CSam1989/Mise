namespace Mise.ApiService.Tables;

internal sealed record CreateSectionRequest(Guid OperationId, string Name, int DisplayOrder);

internal sealed record UpdateSectionRequest(Guid OperationId, string Name, int DisplayOrder);

internal sealed record DeactivateSectionRequest(Guid OperationId);
