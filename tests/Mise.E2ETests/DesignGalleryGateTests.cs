using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Mise.E2ETests;

/// <summary>Host-level, not browser-level: the gallery's Development-only gate is a middleware decision made before auth.</summary>
[Trait("Category", "E2E")]
public class DesignGalleryGateTests
{
    private static async Task<HttpResponseMessage> GetDesignAsync(string environment)
    {
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b.UseEnvironment(environment));
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        return await client.GetAsync("/design", TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Design_InProduction_Returns404BeforeAuth()
    {
        using var response = await GetDesignAsync("Production");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound,
            because: "the gallery hosts preview fakes and must not exist at all outside Development, not even as a login redirect.");
    }

    [Fact]
    public async Task Design_InDevelopment_RequiresSignIn()
    {
        using var response = await GetDesignAsync("Development");

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.OriginalString.Should().Contain("/login",
            because: "in Development the route exists but still sits behind the cookie sign-in like every other page.");
    }
}
