using Microsoft.AspNetCore.Hosting;

namespace Mise.IntegrationTests;

[Trait("Category", "Integration")]
public class HealthEndpointTests(CustomWebApplicationFactory factory) : IClassFixture<CustomWebApplicationFactory>
{
    private static CancellationToken CT => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Health_AnonymousRequest_Returns200()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/health", CT);

        response.StatusCode.Should().Be(HttpStatusCode.OK, because: "the fallback authorization policy must exempt /health via AllowAnonymous.");
    }

    [Fact]
    public async Task Health_ProductionEnvironment_Returns200()
    {
        await using var productionFactory = factory.WithWebHostBuilder(builder => builder.UseEnvironment("Production"));
        var client = productionFactory.CreateClient();

        var response = await client.GetAsync("/health", CT);

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            because: "Mise.ServiceDefaults maps /health outside Development on purpose (Phase 0 task 0.6) — Aspire's WithHttpHealthCheck and any real deployment probe both need it there too.");
    }
}
