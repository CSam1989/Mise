namespace Mise.ArchitectureTests;

/// <summary>
/// Rules 13-14 from CLAUDE.md / docs/plan.md's tier-5 list — these police the tests
/// themselves. Runs against every test assembly that exists today; a new test project
/// (Client.UnitTests, IntegrationTests, E2ETests) is added to <see cref="TestAssemblies"/>
/// in the same commit that creates it.
/// </summary>
[Trait("Category", "Architecture")]
public class TestHygieneTests
{
    private static IReadOnlyList<System.Reflection.Assembly> TestAssemblies =>
        [System.Reflection.Assembly.Load("Mise.UnitTests")];

    [Fact]
    public void NoHandlerTests_ReferencesADbContextTypeOrUseInMemoryDatabase()
    {
        var result = Types.InAssemblies(TestAssemblies)
            .That()
            .HaveNameEndingWith("HandlerTests")
            .Should()
            .NotHaveDependencyOnAny(
                "Microsoft.EntityFrameworkCore.DbContext",
                "Microsoft.EntityFrameworkCore.DbContextOptions",
                "Microsoft.EntityFrameworkCore.InMemory")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: "a *HandlerTests type that reaches for a database has stopped being a unit test — it mocks the gateway port instead.");
    }

    [Fact]
    public void NoTestAssembly_CallsDateTimeNowDirectly()
    {
        var offenders = IlCallSiteScanner.FindTypesCalling(TestAssemblies, "System.DateTime", "get_Now")
            .Concat(IlCallSiteScanner.FindTypesCalling(TestAssemblies, "System.DateTime", "get_UtcNow"))
            .Concat(IlCallSiteScanner.FindTypesCalling(TestAssemblies, "System.DateTimeOffset", "get_Now"))
            .Distinct()
            .ToArray();

        offenders.Should().BeEmpty(
            because: "tests that need time use FakeTimeProvider — a test built on the real clock is either flaky or lying about what it proves.");
    }
}
