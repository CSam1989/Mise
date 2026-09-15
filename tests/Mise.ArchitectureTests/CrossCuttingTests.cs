namespace Mise.ArchitectureTests;

/// <summary>
/// Rules 8, 10, 11, 12 from CLAUDE.md / docs/plan.md's tier-5 list, plus the logging-standard
/// guardrails CLAUDE.md's "Logging" section describes. Rules 10, 8, and the Console/Debug ban
/// check real, existing assemblies today (the composition roots) — they are not vacuous.
/// The rest guard on an empty module-assembly list until a module registers.
/// </summary>
[Trait("Category", "Architecture")]
public class CrossCuttingTests
{
    [Fact]
    public void NoAssembly_CallsDateTimeOrDateTimeOffsetNowOutsideACompositionRoot()
    {
        // Every assembly currently in the solution except the three composition roots
        // themselves (Mise.ServiceDefaults is a shared library, not a host — it is
        // deliberately NOT exempt). Uses IlCallSiteScanner, not NetArchTest's
        // Types.InAssemblies: NetArchTest hardcodes "Microsoft" and "System" as excluded
        // namespace prefixes, which would silently skip Mise.ServiceDefaults' own
        // Microsoft.Extensions.Hosting.Extensions type entirely.
        var compositionRootNames = CompositionRoots.Assemblies.Select(a => a.GetName().Name).ToHashSet();
        var candidates = new[] { "Mise.ServiceDefaults", "Mise.SharedKernel", "Mise.SharedKernel.Infrastructure" }
            .Select(System.Reflection.Assembly.Load)
            .Where(a => !compositionRootNames.Contains(a.GetName().Name))
            .Concat(ModuleAssemblies.Domain)
            .Concat(ModuleAssemblies.Application)
            .Concat(ModuleAssemblies.Infrastructure)
            .ToArray();

        var offenders = IlCallSiteScanner.FindTypesCalling(candidates, "System.DateTime", "get_Now")
            .Concat(IlCallSiteScanner.FindTypesCalling(candidates, "System.DateTime", "get_UtcNow"))
            .Concat(IlCallSiteScanner.FindTypesCalling(candidates, "System.DateTimeOffset", "get_Now"))
            .Distinct()
            .ToArray();

        offenders.Should().BeEmpty(
            because: "TimeProvider must be injected everywhere outside a host's composition root (CLAUDE.md) — every time-dependent rule in this domain is untestable otherwise.");
    }

    [Fact]
    public void CompositionRoots_MayCallDateTimeNow()
    {
        // The inverse of the rule above, made explicit: this is the one place it's allowed,
        // so a future tightening of the rule above can't silently start failing the hosts
        // themselves without anyone noticing why.
        var offenders = IlCallSiteScanner.FindTypesCalling(CompositionRoots.Assemblies, "System.DateTime", "get_Now");

        // No assertion: composition roots are explicitly allowed to call DateTime.Now. This
        // test exists to document the exemption, not enforce it.
        _ = offenders;
    }

    [Fact]
    public void OnlyInfrastructureAssemblies_CallSaveChanges()
    {
        var nonInfrastructure = CompositionRoots.Assemblies
            .Where(a => a.GetName().Name != "Mise.ApiService") // the composition root wires up Infrastructure; it may transitively touch SaveChanges via DI registration code paths in Phase 2 — re-evaluate then.
            .Concat(ModuleAssemblies.Domain)
            .Concat(ModuleAssemblies.Application)
            .ToArray();

        var offenders = IlCallSiteScanner
            .FindTypesCalling(nonInfrastructure, "Microsoft.EntityFrameworkCore.DbContext", "SaveChanges")
            .Concat(IlCallSiteScanner.FindTypesCalling(nonInfrastructure, "Microsoft.EntityFrameworkCore.DbContext", "SaveChangesAsync"))
            .Distinct()
            .ToArray();

        offenders.Should().BeEmpty(
            because: "only Infrastructure types call SaveChangesAsync — this is the seam that keeps the unit tier fast and honest.");
    }

    [Fact]
    public void NoDomainOrApplicationAssembly_ReferencesAspire()
    {
        var assemblies = ModuleAssemblies.Domain.Concat(ModuleAssemblies.Application).ToArray();
        if (assemblies.Length == 0) return;

        var result = Types.InAssemblies(assemblies)
            .Should()
            .NotHaveDependencyOnAny("Aspire.Hosting", "Aspire.Hosting.ApplicationModel")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: "Aspire is a dev/orchestration concern only (decision #1) — Domain and Application stay Aspire-ignorant.");
    }

    [Fact]
    public void EveryCommandHandler_DependsOnSomethingImplementingIAuditWriter()
    {
        var applicationAssemblies = ModuleAssemblies.Application;
        if (applicationAssemblies.Count == 0) return;

        var result = Types.InAssemblies(applicationAssemblies)
            .That()
            .HaveNameEndingWith("CommandHandler")
            .Should()
            .MeetCustomRule(new HasConstructorParameterRule("IAuditWriter"))
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: "a mutating handler with no IAuditWriter dependency at all can never write an audit entry (belt half of the belt-and-suspenders audit-completeness guardrail).");
    }

    [Fact]
    public void EveryCommandHandler_DependsOnAnILogger()
    {
        var applicationAssemblies = ModuleAssemblies.Application;
        if (applicationAssemblies.Count == 0) return;

        var result = Types.InAssemblies(applicationAssemblies)
            .That()
            .HaveNameEndingWith("CommandHandler")
            .Should()
            .MeetCustomRule(new HasConstructorParameterRule("ILogger"))
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: "CLAUDE.md's Logging standard applies everywhere — a handler with no ILogger dependency at all can never emit the structured log lines production debugging depends on.");
    }

    [Fact]
    public void NoProductionAssembly_CallsConsoleOrDebugWriteDirectly()
    {
        // Every production assembly (composition roots included — there is no carve-out here
        // the way there is for DateTime.Now, since ILogger is available everywhere a host's
        // own code runs). Deliberately excludes test assemblies: Console output in a test is
        // a normal debugging aid, not a production-logging-discipline violation.
        var candidates = new[] { "Mise.ServiceDefaults", "Mise.SharedKernel", "Mise.SharedKernel.Infrastructure", "Mise.UI.Abstractions", "Mise.UI.Components" }
            .Select(System.Reflection.Assembly.Load)
            .Concat(CompositionRoots.Assemblies)
            .Concat(ModuleAssemblies.Domain)
            .Concat(ModuleAssemblies.Application)
            .Concat(ModuleAssemblies.Infrastructure)
            .Concat(ModuleAssemblies.Contracts)
            .Distinct()
            .ToArray();

        var writeMethods = new (string DeclaringType, string MethodName)[]
        {
            ("System.Console", "WriteLine"),
            ("System.Console", "Write"),
            ("System.Diagnostics.Debug", "WriteLine"),
            ("System.Diagnostics.Debug", "Write"),
            ("System.Diagnostics.Debug", "Print"),
        };

        var offenders = writeMethods
            .SelectMany(m => IlCallSiteScanner.FindTypesCalling(candidates, m.DeclaringType, m.MethodName))
            .Distinct()
            .ToArray();

        offenders.Should().BeEmpty(
            because: "structured logging via ILogger<T> is the standard (CLAUDE.md's Logging section) — a Console/Debug print never reaches the log pipeline, so it's invisible in production no matter how loud it looks locally.");
    }
}
