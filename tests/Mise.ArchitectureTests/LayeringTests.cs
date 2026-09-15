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
