using System.Reflection;

namespace Mise.ArchitectureTests;

/// <summary>
/// The only assemblies allowed to read the wall clock directly (CLAUDE.md's TimeProvider
/// rule) or reference Aspire hosting types. Loaded by simple name rather than through a
/// public marker type in each project, so this list doesn't depend on any of those hosts
/// exposing a particular public type.
/// </summary>
internal static class CompositionRoots
{
    public static readonly IReadOnlyList<Assembly> Assemblies =
    [
        Assembly.Load("Mise.ApiService"),
        Assembly.Load("Mise.Web"),
        Assembly.Load("Mise.AppHost"),
    ];
}
