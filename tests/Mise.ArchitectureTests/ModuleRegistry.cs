namespace Mise.ArchitectureTests;

/// <summary>
/// Hand-maintained list of modules that exist today. CLAUDE.md's rule: a new module is
/// added here, gets a ProjectReference from this test project (see the .csproj), and gets
/// referenced from Mise.ApiService's composition root — all in the same commit. That
/// three-way agreement is what <see cref="ModuleBoundaryTests"/>'s registry rule checks.
/// </summary>
internal static class ModuleRegistry
{
    public static readonly IReadOnlyList<string> Modules =
    [
        "Reservations",
        "StaffIdentity",
        "Tables",
        "Scheduling",
    ];
}
