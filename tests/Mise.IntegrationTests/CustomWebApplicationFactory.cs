extern alias ApiService;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Mise.ServiceDefaults;

namespace Mise.IntegrationTests;

/// <summary>
/// Wraps Mise.ApiService for endpoint-level integration tests. Supplies safe, non-functional
/// defaults for configuration Mise.ApiService now requires at startup regardless of which
/// endpoint a test exercises — the JWT signing key, and a syntactically valid but
/// unreachable misedb connection string. UseSetting (not ConfigureAppConfiguration) is what
/// actually lands in time: Mise.ApiService's top-level Program.cs reads builder.Configuration
/// synchronously before builder.Build() runs, and ConfigureAppConfiguration's extra source
/// isn't merged in until Build() — UseSetting's value is visible from the start.
/// ReservationsApiFixture layers a real Testcontainers connection string on top via
/// WithWebHostBuilder, which wins because UseSetting overwrites by key.
/// </summary>
public sealed class CustomWebApplicationFactory : WebApplicationFactory<ApiService::Program>
{
    public const string TestSigningKey = "integration-test-signing-key-do-not-use-in-production-3242";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting(PlaceholderAuthDefaults.SigningKeyConfigKey, TestSigningKey);
        builder.UseSetting("ConnectionStrings:misedb", "Host=localhost;Database=misedb-unused;Username=postgres;Password=postgres");
    }
}
