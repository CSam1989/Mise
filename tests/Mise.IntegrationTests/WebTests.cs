using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Logging;

namespace Mise.IntegrationTests;

[Trait("Category", "Integration")]
public class WebTests
{
    // The actual root cause of every prior "TimeoutException" in CI, found only once
    // XunitTestOutputLoggerProvider surfaced the AppHost's own logs: apiservice's health
    // check targets its HTTPS endpoint (Aspire's WithHttpHealthCheck defaults to it), using
    // the ASP.NET Core developer certificate — trusted on a real dev machine via `dotnet
    // dev-certs https --trust` (README's local setup), but never trusted on an ephemeral CI
    // runner. The health check failed with AuthenticationException: UntrustedRoot on every
    // single attempt, forever — no amount of extra timeout could ever have fixed it, only
    // hidden how deterministic the failure actually was. No timeout bump was the real fix;
    // this is.
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(120);

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
            // Every HttpClient built through this factory — including the one Aspire's own
            // WithHttpHealthCheck uses internally (confirmed from the failing test's captured
            // log: its call stack runs through this exact ResilienceHandler pipeline) — must
            // tolerate the untrusted-in-CI dev certificate described above. Loopback traffic
            // between processes the CI runner itself just started; there is no real party to
            // impersonate and nothing this validation would actually protect against here.
            // Scoped to this test's own throwaway AppHost instance only — never a pattern for
            // Mise.ApiService/Mise.Web's real Program.cs.
            clientBuilder.ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (_, _, _, _) => true,
            });
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
