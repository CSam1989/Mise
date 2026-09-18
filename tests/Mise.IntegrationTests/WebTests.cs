using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Logging;

namespace Mise.IntegrationTests;

[Trait("Category", "Integration")]
public class WebTests
{
    // 30s was the dotnet-new-aspire-starter template's original default — too tight for a
    // cold CI runner's StartAsync, which pulls the Postgres container image, starts three
    // .NET processes (ApiService/MigrationService/Web), and runs EF migrations, all before
    // the first health check can pass. 120s wasn't enough either (still a plain
    // TimeoutException, never a connection/container error — the clearest possible signal
    // that this only ever needed more wall-clock time, not a config fix). Bumped again,
    // generously: this is a one-off smoke test ("does the real app graph start?"), not
    // something performance-sensitive, so erring high costs nothing but idle CI minutes.
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(240);

    [Fact]
    public async Task GetWebResourceRootReturnsOkStatusCode()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        var appHost = await DistributedApplicationTestingBuilder.CreateAsync<Projects.Mise_AppHost>(cancellationToken);
        // "jwt-signing-key" has no default (docs/plan.md's Phase 2 placeholder auth spine
        // deliberately never hardcodes it) — `aspire run` prompts for it interactively and
        // persists to user secrets, but this non-interactive test graph needs it supplied
        // directly, the same way a CI pipeline would set it as a real secret.
        appHost.Configuration["Parameters:jwt-signing-key"] = "webtests-fixture-signing-key-value";
        // "seed-manager-password" is likewise a secret Aspire parameter with no default
        // (Phase 3) — Mise.MigrationService fails fast without it, the same way it already
        // did for the signing key above.
        appHost.Configuration["Parameters:seed-manager-password"] = "webtests-fixture-seed-manager-password";
        appHost.Services.AddLogging(logging =>
        {
            logging.SetMinimumLevel(LogLevel.Debug);
            // Override the logging filters from the app's configuration
            logging.AddFilter(appHost.Environment.ApplicationName, LogLevel.Debug);
            logging.AddFilter("Aspire.", LogLevel.Debug);
            // Surfaces the above in the failing test's own output — see
            // XunitTestOutputLoggerProvider's doc comment for why this matters here
            // specifically: a timeout with no captured logs gives no clue which resource
            // stalled, which is exactly what happened debugging this test in CI.
            logging.AddProvider(new XunitTestOutputLoggerProvider());
        });
        appHost.Services.ConfigureHttpClientDefaults(clientBuilder =>
        {
            clientBuilder.AddStandardResilienceHandler();
        });

        await using var app = await appHost.BuildAsync(cancellationToken).WaitAsync(DefaultTimeout, cancellationToken);
        await app.StartAsync(cancellationToken).WaitAsync(DefaultTimeout, cancellationToken);

        // Act
        var httpClient = app.CreateHttpClient("webfrontend");
        await app.ResourceNotifications.WaitForResourceHealthyAsync("webfrontend", cancellationToken).WaitAsync(DefaultTimeout, cancellationToken);
        var response = await httpClient.GetAsync("/", cancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
