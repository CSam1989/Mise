namespace Mise.ApiService.Tables;

internal sealed record CreateTableGroupRequest(Guid OperationId, string Name, IReadOnlyList<Guid> TableIds);
