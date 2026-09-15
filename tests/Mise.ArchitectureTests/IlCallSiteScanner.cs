using System.Reflection;
using Mono.Cecil;

namespace Mise.ArchitectureTests;

/// <summary>
/// Scans compiled assemblies directly with Mono.Cecil for call-sites to a given method,
/// bypassing NetArchTest.Rules' <c>Types.InAssembly</c>/<c>InAssemblies</c> entirely.
///
/// Those entry points silently drop any type whose namespace starts with "System" or
/// "Microsoft" (NetArchTest's own hardcoded exclusion list, meant to keep BCL noise out of
/// Types.InCurrentDomain() scans) — which also drops Mise.ServiceDefaults' own
/// `Microsoft.Extensions.Hosting.Extensions` type, since the Aspire template puts it there
/// deliberately for discoverability. A rule built on Types.InAssembly would pass on that
/// type unconditionally, regardless of what it does, and never tell you why. This scanner
/// has no such exclusion list.
/// </summary>
internal static class IlCallSiteScanner
{
    public static IReadOnlyList<string> FindTypesCalling(
        IEnumerable<Assembly> assemblies, string declaringTypeFullName, string methodName)
    {
        var matches = new List<string>();
        foreach (var assembly in assemblies)
        {
            using var moduleDef = ModuleDefinition.ReadModule(assembly.Location);
            foreach (var type in AllTypes(moduleDef.Types))
            {
                var rule = new DoesNotCallMethodRule(declaringTypeFullName, methodName);
                if (!rule.MeetsRule(type))
                {
                    matches.Add(type.FullName);
                }
            }
        }
        return matches;
    }

    private static IEnumerable<TypeDefinition> AllTypes(IEnumerable<TypeDefinition> types)
    {
        foreach (var type in types)
        {
            yield return type;
            foreach (var nested in AllTypes(type.NestedTypes))
            {
                yield return nested;
            }
        }
    }
}
