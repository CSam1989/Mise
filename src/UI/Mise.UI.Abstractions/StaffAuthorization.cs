namespace Mise.UI.Abstractions;

public static class StaffRoles
{
    public const string FloorStaff = "FloorStaff";
    public const string Manager = "Manager";
}

/// <summary>Same names and semantics as Mise.ApiService's policies: FloorStaff admits a Manager too.</summary>
public static class StaffPolicies
{
    public const string FloorStaff = "FloorStaff";
    public const string Manager = "Manager";
}
