using Mono.Cecil;

namespace Mise.ArchitectureTests;

/// <summary>
/// Passes if the type has at least one constructor with a parameter whose type name
/// contains the given substring. Matched by name rather than by resolving the actual
/// interface type, since IAuditWriter doesn't exist as a compiled type this project can
/// reference until Mise.SharedKernel.Infrastructure is built (Phase 2+).
/// </summary>
internal sealed class HasConstructorParameterRule(string parameterTypeNameContains) : ICustomRule
{
    public bool MeetsRule(TypeDefinition type) =>
        type.Methods
            .Where(m => m.IsConstructor)
            .Any(ctor => ctor.Parameters.Any(p => p.ParameterType.Name.Contains(parameterTypeNameContains, StringComparison.Ordinal)));
}
