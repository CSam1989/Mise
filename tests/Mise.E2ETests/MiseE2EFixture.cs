extern alias ApiService;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Mise.E2ETests.Infrastructure;
using Mise.E2ETests.Pages;
using Mise.Modules.Reservations.Infrastructure.Persistence;
using Mise.Modules.StaffIdentity.Infrastructure;
using Mise.Modules.StaffIdentity.Infrastructure.Persistence;
using Mise.ServiceDefaults;
using Testcontainers.PostgreSql;

namespace Mise.E2ETests;

/// <summary>
/// Boots Mise.ApiService and Mise.Web on real Kestrel sockets, backed by a real
/// Testcontainers Postgres — the full path Playwright needs to prove a reservation created
/// in the browser lands in the database (docs/plan.md Phase 2's exit criterion). Named for
/// the hosts it boots, not any one module (it was ReservationsE2EFixture through Phase 2).
/// Mise.Web's service-discovery configuration ("services:apiservice:http:0") is pointed at
/// the real ApiService socket the same way Aspire wires it at runtime, so
/// HttpReservationsClient's "https+http://apiservice" base address resolves without booting a
/// real AppHost. Seeds every <see cref="E2EUsers"/> account (the Manager via the seeder, the
/// rest through the real Manager-only POST /api/staff), then signs the Manager and Floor Staff
/// in once through the real login page and keeps each browser session, so tests that aren't
/// about signing in start already signed in (Playwright's storage-state practice).
/// </summary>
public sealed class MiseE2EFixture : IAsyncLifetime, IBrowserHost
{
    private const string SigningKey = "e2e-fixture-signing-key-do-not-use-in-production-93482";

    private PostgreSqlContainer? _postgres;
    private KestrelFactory<ApiService::Program>? _apiFactory;
    private KestrelFactory<Program>? _webFactory;
    private IPlaywright? _playwright;

    public IBrowser Browser { get; private set; } = null!;
    public string BaseUrl { get; private set; } = "";

    public string ManagerSession { get; private set; } = "";
    public string FloorStaffSession { get; private set; } = "";

    public async ValueTask InitializeAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        _postgres = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await _postgres.StartAsync(cancellationToken);

        _apiFactory = new KestrelFactory<ApiService::Program>(builder =>
        {
            builder.UseSetting(JwtAuthDefaults.SigningKeyConfigKey, SigningKey);
            builder.UseSetting("ConnectionStrings:misedb", _postgres.GetConnectionString());
        });
        var apiBaseUrl = _apiFactory.Start();

        using (var scope = _apiFactory.Services.CreateScope())
        {
            var reservationsDb = scope.ServiceProvider.GetRequiredService<ReservationsDbContext>();
            await reservationsDb.Database.MigrateAsync(cancellationToken);

            var staffIdentityDb = scope.ServiceProvider.GetRequiredService<StaffIdentityDbContext>();
            await staffIdentityDb.Database.MigrateAsync(cancellationToken);

            var seeder = scope.ServiceProvider.GetRequiredService<StaffIdentitySeeder>();
            await seeder.EnsureManagerExistsAsync(
                E2EUsers.Manager.Username, E2EUsers.Manager.Password, E2EUsers.Manager.FullName, cancellationToken);
        }

        await RegisterThroughTheApiAsync(apiBaseUrl, [E2EUsers.FloorStaff, E2EUsers.SignOut], cancellationToken);

        // No JWT signing key here — Mise.Web never mints a token itself (Phase 3); it only
        // ever holds one the API already issued via a real login.
        _webFactory = new KestrelFactory<Program>(builder =>
        {
            builder.UseSetting("services:apiservice:http:0", apiBaseUrl);
        });
        BaseUrl = _webFactory.Start();

        (_playwright, Browser) = await PlaywrightBrowser.LaunchAsync();

        ManagerSession = await SignInOnceAsync(E2EUsers.Manager);
        FloorStaffSession = await SignInOnceAsync(E2EUsers.FloorStaff);
    }

    private async Task<string> SignInOnceAsync(E2EUser user)
    {
        await using var context = await Browser.NewContextAsync(new BrowserNewContextOptions { BaseURL = BaseUrl });
        var page = await context.NewPageAsync();

        var login = await new LoginPage(page).GotoAsync();
        await login.SignInAsync(user);

        return await context.StorageStateAsync();
    }

    private static async Task RegisterThroughTheApiAsync(string apiBaseUrl, IEnumerable<E2EUser> users, CancellationToken cancellationToken)
    {
        using var api = new HttpClient { BaseAddress = new Uri(apiBaseUrl) };

        using var login = await api.PostAsJsonAsync(
            "/api/auth/login", new { E2EUsers.Manager.Username, E2EUsers.Manager.Password }, cancellationToken);
        login.EnsureSuccessStatusCode();
        using var loginBody = JsonDocument.Parse(await login.Content.ReadAsStringAsync(cancellationToken));
        api.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", loginBody.RootElement.GetProperty("token").GetString());

        foreach (var user in users)
        {
            using var register = await api.PostAsJsonAsync("/api/staff", new
            {
                OperationId = Guid.NewGuid(),
                user.Username,
                user.Password,
                user.FullName,
                user.Role,
            }, cancellationToken);
            register.EnsureSuccessStatusCode();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await Browser.CloseAsync();
        _playwright?.Dispose();

        if (_webFactory is not null)
        {
            await _webFactory.DisposeAsync();
        }

        if (_apiFactory is not null)
        {
            await _apiFactory.DisposeAsync();
        }

        if (_postgres is not null)
        {
            await _postgres.DisposeAsync();
        }
    }
}

[CollectionDefinition(Name)]
public sealed class MiseE2ECollection : ICollectionFixture<MiseE2EFixture>
{
    public const string Name = "MiseE2E";
}
