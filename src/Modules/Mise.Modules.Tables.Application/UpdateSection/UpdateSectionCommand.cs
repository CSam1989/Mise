namespace Mise.Modules.Tables.Application.UpdateSection;

public sealed record UpdateSectionCommand(
    Guid OperationId, Guid SectionId, string Name, int DisplayOrder, Guid PerformedByStaffId);
