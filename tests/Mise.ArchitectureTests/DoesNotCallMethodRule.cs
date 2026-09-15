using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Mise.ArchitectureTests;

/// <summary>
/// Fails a type if any of its methods contain a call-site to the given method (matched by
/// declaring-type full name + method name). NetArchTest's built-in dependency rules work
/// at the type-reference level (does this type mention that type anywhere); catching
/// "calls DateTime.get_Now" or "calls DbContext.SaveChangesAsync" specifically — not just
/// any use of DateTime or DbContext as a type — needs an IL call-site scan instead.
/// </summary>
internal sealed class DoesNotCallMethodRule(string declaringTypeFullName, string methodName) : ICustomRule
{
    public bool MeetsRule(TypeDefinition type) =>
        !type.Methods
            .Where(m => m.HasBody)
            .SelectMany(m => m.Body.Instructions)
            .Where(i => i.OpCode == OpCodes.Call || i.OpCode == OpCodes.Callvirt)
            .Select(i => i.Operand as MethodReference)
            .Any(mr => mr is not null
                       && mr.Name == methodName
                       && mr.DeclaringType.FullName == declaringTypeFullName);
}
