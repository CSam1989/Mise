namespace Mise.Modules.StaffIdentity.Domain;

/// <summary>
/// Exactly two roles for v1 (charter §"Two-tier roles"): more granular roles can be added
/// later without a redesign since authorization is policy-based, not hardcoded. The
/// "Manager" policy accepts only Manager; the "FloorStaff" policy accepts either role
/// (ADR-001 §Auth: "Manager policy implies Floor Staff permissions").
/// </summary>
public enum StaffRole
{
    FloorStaff,
    Manager,
}
