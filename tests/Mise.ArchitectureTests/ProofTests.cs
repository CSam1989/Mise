namespace Mise.ArchitectureTests;

[Trait("Category", "Architecture")]
public class ProofTests
{
    [Fact]
    public void CompositionRoots_LoadByName_ResolveToRealAssemblies()
    {
        CompositionRoots.Assemblies.Should().HaveCount(4);
        CompositionRoots.Assemblies.Select(a => a.GetName().Name)
            .Should().BeEquivalentTo(["Mise.ApiService", "Mise.Web", "Mise.AppHost", "Mise.MigrationService"]);
    }
}
