namespace Mise.Modules.Tables.Contracts;

public sealed record TableGroupDto(Guid Id, string Name, bool IsActive, IReadOnlyList<Guid> TableIds);
