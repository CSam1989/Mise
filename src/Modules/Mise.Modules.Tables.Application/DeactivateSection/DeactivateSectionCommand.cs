namespace Mise.Modules.Tables.Application.DeactivateSection;

public sealed record DeactivateSectionCommand(Guid OperationId, Guid SectionId, Guid PerformedByStaffId);
