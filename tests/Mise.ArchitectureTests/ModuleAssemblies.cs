using System.Reflection;

namespace Mise.ArchitectureTests;

/// <summary>
/// Loads a registered module's layer assemblies by the naming convention
/// Mise.Modules.{Module}.{Layer}. Only modules listed in <see cref="ModuleRegistry"/> are
/// looked up, so a rule below stays vacuously true — not accidentally broken — until a
/// module is actually registered.
/// </summary>
internal static class ModuleAssemblies
{
    private static Assembly? TryLoad(string name)
    {
        try
        {
            return Assembly.Load(name);
        }
        catch (Exception ex) when (ex is FileNotFoundException or FileLoadException)
        {
            return null;
        }
    }

    public static IReadOnlyList<Assembly> ForLayer(string layer) =>
        ModuleRegistry.Modules
            .Select(module => TryLoad($"Mise.Modules.{module}.{layer}"))
            .OfType<Assembly>()
            .ToArray();

    public static IReadOnlyList<Assembly> Domain => ForLayer("Domain");
    public static IReadOnlyList<Assembly> Application => ForLayer("Application");
    public static IReadOnlyList<Assembly> Infrastructure => ForLayer("Infrastructure");
    public static IReadOnlyList<Assembly> Contracts => ForLayer("Contracts");
}
