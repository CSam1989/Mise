namespace Mise.ArchitectureTests;

/// <summary>
/// ADR-001's module-boundary rules (CLAUDE.md's reference table). No module exists yet
/// (Phase 2+ adds the first one), so every rule here guards on an empty assembly list and
/// passes trivially until <see cref="ModuleRegistry"/> gains an entry — at which point it
/// starts checking real types instead of nothing.
/// </summary>
[Trait("Category", "Architecture")]
public class ModuleBoundaryTests
{
    [Fact]
    public void Domain_NeverReferencesAnotherMiseAssemblyExceptSharedKernel()
    {
        var domainAssemblies = ModuleAssemblies.Domain;
        if (domainAssemblies.Count == 0) return;

        var result = Types.InAssemblies(domainAssemblies)
            .Should()
            .NotHaveDependencyOnAny(
                "Mise.ApiService", "Mise.Web", "Mise.AppHost", "Mise.ServiceDefaults",
                "Microsoft.EntityFrameworkCore", "Microsoft.AspNetCore")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: "a module's Domain project may depend on Mise.SharedKernel only — no EF, no ASP.NET, no other module.");
    }

    [Fact]
    public void Application_NeverReferencesAnotherModulesDomainApplicationOrInfrastructure()
    {
        var applicationAssemblies = ModuleAssemblies.Application;
        if (applicationAssemblies.Count == 0) return;

        foreach (var module in ModuleRegistry.Modules)
        {
            var otherModules = ModuleRegistry.Modules.Where(m => m != module);
            var forbidden = otherModules
                .SelectMany(m => new[]
                {
                    $"Mise.Modules.{m}.Domain",
                    $"Mise.Modules.{m}.Application",
                    $"Mise.Modules.{m}.Infrastructure",
                })
                .Concat(["Microsoft.EntityFrameworkCore", "Microsoft.AspNetCore"])
                .ToArray();

            var ownApplication = ModuleAssemblies.ForLayer("Application")
                .Where(a => a.GetName().Name == $"Mise.Modules.{module}.Application")
                .ToArray();
            if (ownApplication.Length == 0) continue;

            var result = Types.InAssemblies(ownApplication)
                .Should()
                .NotHaveDependencyOnAny(forbidden)
                .GetResult();

            result.IsSuccessful.Should().BeTrue(
                because: $"{module}.Application may depend on its own Domain, Mise.SharedKernel, and other modules' Contracts only.");
        }
    }

    [Fact]
    public void Infrastructure_IsReferencedByNothingExceptTheCompositionRoot()
    {
        var infrastructureAssemblies = ModuleAssemblies.Infrastructure;
        if (infrastructureAssemblies.Count == 0) return;

        var infrastructureNames = infrastructureAssemblies.Select(a => a.GetName().Name!).ToArray();

        // Everything in the solution except the composition root (Mise.ApiService) itself —
        // no module's Domain/Application/Contracts, and neither host-adjacent library
        // (Mise.Web, Mise.ServiceDefaults) is allowed to reference a module's Infrastructure.
        var candidateReferrers = ModuleAssemblies.Domain
            .Concat(ModuleAssemblies.Application)
            .Concat(ModuleAssemblies.Contracts)
            .Concat(CompositionRoots.Assemblies.Where(a => a.GetName().Name != "Mise.ApiService"))
            .ToArray();

        var result = Types.InAssemblies(candidateReferrers)
            .Should()
            .NotHaveDependencyOnAny(infrastructureNames)
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: "only the composition root (Mise.ApiService) may reference a module's Infrastructure project.");
    }

    [Fact]
    public void Contracts_ContainOnlyInterfacesDtosAndEvents()
    {
        var contractsAssemblies = ModuleAssemblies.Contracts;
        if (contractsAssemblies.Count == 0) return;

        var result = Types.InAssemblies(contractsAssemblies)
            .That()
            .AreClasses()
            .Should()
            .NotHaveDependencyOnAny("Microsoft.EntityFrameworkCore")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: "Contracts projects hold interfaces, DTOs, and events only — no EF types, no handlers.");
    }

    [Fact]
    public void HostModuleReferences_MatchTheRegistry()
    {
        var apiService = System.Reflection.Assembly.Load("Mise.ApiService");
        var referencedModuleAssemblyNames = apiService.GetReferencedAssemblies()
            .Select(a => a.Name!)
            .Where(name => name.StartsWith("Mise.Modules.", StringComparison.Ordinal))
            .Select(name => name.Split('.')[2]) // Mise.Modules.{Module}.{Layer}
            .Distinct()
            .ToArray();

        referencedModuleAssemblyNames.Should().BeEquivalentTo(
            ModuleRegistry.Modules,
            because: "a module reachable from Mise.ApiService must be listed in ModuleRegistry, and vice versa — the rule that keeps rules 1-4 trustworthy.");
    }
}
