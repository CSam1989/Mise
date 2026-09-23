using Mise.SharedKernel.Infrastructure;

namespace Mise.ArchitectureTests;

/// <summary>
/// Rules 6, 7 and 9 from CLAUDE.md / docs/plan.md's tier-5 list — layering within a single
/// module, once one exists. All three guard on an empty assembly list until Phase 2
/// registers the first module in <see cref="ModuleRegistry"/>.
/// </summary>
[Trait("Category", "Architecture")]
public class LayeringTests
{
    [Fact]
    public void Domain_NeverReferencesEfCore()
    {
        var domainAssemblies = ModuleAssemblies.Domain;
        if (domainAssemblies.Count == 0) return;

        var result = Types.InAssemblies(domainAssemblies)
            .Should()
            .NotHaveDependencyOnAny("Microsoft.EntityFrameworkCore")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: "Domain types reference no EF Core type (DbContext, DbSet<>, or anything in the Microsoft.EntityFrameworkCore namespace).");
    }

    [Fact]
    public void ApplicationHandlers_NeverDependOnDbContextDirectly()
    {
        var applicationAssemblies = ModuleAssemblies.Application;
        if (applicationAssemblies.Count == 0) return;

        var result = Types.InAssemblies(applicationAssemblies)
            .That()
            .HaveNameEndingWith("Handler")
            .Should()
            .NotHaveDependencyOnAny("Microsoft.EntityFrameworkCore.DbContext", "Microsoft.EntityFrameworkCore")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: "handlers depend on the slice-specific gateway port interface, never on DbContext — this is the seam that keeps the unit tier fast.");
    }

    /// <summary>
    /// CLAUDE.md's cross-cutting infrastructure section claims Mise.SharedKernel.Infrastructure
    /// "must stay EF-free forever" (every module's Application project references it directly
    /// for IAuditWriter/IAuditReader — adding EF Core there would leak EF onto every Application
    /// project transitively). Found during Phase 9 planning that nothing actually enforced this
    /// claim at runtime — only the physical project split (no EF package reference) and review
    /// discipline. Unlike rules 1-9, this scans a specific assembly directly rather than
    /// ModuleAssemblies, since Mise.SharedKernel.Infrastructure isn't a per-module assembly.
    /// </summary>
    [Fact]
    public void SharedKernelInfrastructure_NeverReferencesEfCore()
    {
        var result = Types.InAssembly(typeof(IAuditWriter).Assembly)
            .Should()
            .NotHaveDependencyOnAny("Microsoft.EntityFrameworkCore")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: "Mise.SharedKernel.Infrastructure must stay EF-free forever — every module's Application project " +
            "references it directly for IAuditWriter/IAuditReader, so any EF Core dependency here would leak onto " +
            "every Application project transitively, exactly what the module boundary table forbids.");
    }

    [Fact]
    public void HandlersAreSealed()
    {
        var applicationAssemblies = ModuleAssemblies.Application;
        if (applicationAssemblies.Count == 0) return;

        var result = Types.InAssemblies(applicationAssemblies)
            .That()
            .HaveNameEndingWith("Handler")
            .Should()
            .BeSealed()
            .GetResult();

        result.IsSuccessful.Should().BeTrue(because: "handlers are sealed — there is exactly one implementation per use case.");
    }
}
