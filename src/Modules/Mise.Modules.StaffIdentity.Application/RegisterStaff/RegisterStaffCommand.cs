using Mise.Modules.StaffIdentity.Domain;

namespace Mise.Modules.StaffIdentity.Application.RegisterStaff;

/// <summary>
/// <paramref name="PerformedByStaffId"/> is the Manager performing the registration — the
/// endpoint's Manager-only policy already proved that role server-side; this is only for
/// attribution on the resulting audit entry.
/// </summary>
public sealed record RegisterStaffCommand(
    Guid OperationId,
    string Username,
    string Password,
    string FullName,
    StaffRole Role,
    Guid PerformedByStaffId);
