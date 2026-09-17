extern alias ApiService;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
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
/// real AppHost. Also seeds one Manager account so E2E tests can sign in for real, the same
/// login a staff member would use.
/// </summary>
public sealed class MiseE2EFixture : IAsyncLifetime
{
    private const string SigningKey = "e2e-fixture-signing-key-do-not-use-in-production-93482";

    public const string SeedManagerUsername = "e2e-manager";
    public const string SeedManagerPassword = "E2E-Manager-Password1";

    private PostgreSqlContainer? _postgres;
    private KestrelFactory<ApiService::Program>? _apiFactory;
    private KestrelFactory<Program>? _webFactory;
    private IPlaywright? _playwright;

    public IBrowser Browser { get; private set; } = null!;
    public string BaseUrl { get; private set; } = "";

    public async ValueTask InitializeAsync()
    {
        _postgres = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await _postgres.StartAsync();

        _apiFactory = new KestrelFactory<ApiService::Program>(builder =>
        {
            builder.UseSetting(JwtAuthDefaults.SigningKeyConfigKey, SigningKey);
            builder.UseSetting("ConnectionStrings:misedb", _postgres.GetConnectionString());
        });
        var apiBaseUrl = _apiFactory.Start();

        using (var scope = _apiFactory.Services.CreateScope())
        {
            var reservationsDb = scope.ServiceProvider.GetRequiredService<ReservationsDbContext>();
            await reservationsDb.Database.MigrateAsync(TestContext.Current.CancellationToken);

            var staffIdentityDb = scope.ServiceProvider.GetRequiredService<StaffIdentityDbContext>();
            await staffIdentityDb.Database.MigrateAsync(TestContext.Current.CancellationToken);

            var seeder = scope.ServiceProvider.GetRequiredService<StaffIdentitySeeder>();
            await seeder.EnsureManagerExistsAsync(
                SeedManagerUsername, SeedManagerPassword, "E2E Manager", TestContext.Current.CancellationToken);
        }

        // No JWT signing key here — Mise.Web never mints a token itself (Phase 3); it only
        // ever holds one the API already issued via a real login.
        _webFactory = new KestrelFactory<Program>(builder =>
        {
            builder.UseSetting("services:apiservice:http:0", apiBaseUrl);
        });
        BaseUrl = _webFactory.Start();

        _playwright = await Playwright.CreateAsync();
        Browser = await _playwright.Chromium.LaunchAsync();
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
