namespace Mise.Modules.Tables.Application.CreateSection;

public sealed record CreateSectionCommand(Guid OperationId, string Name, int DisplayOrder, Guid PerformedByStaffId);
