extern alias ApiService;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Mise.Modules.Reservations.Infrastructure.Persistence;
using Mise.ServiceDefaults;
using Testcontainers.PostgreSql;

namespace Mise.E2ETests;

/// <summary>
/// Boots Mise.ApiService and Mise.Web on real Kestrel sockets, backed by a real
/// Testcontainers Postgres — the full path Playwright needs to prove a reservation created
/// in the browser lands in the database (docs/plan.md Phase 2's exit criterion). Mise.Web's
/// service-discovery configuration ("services:apiservice:http:0") is pointed at the real
/// ApiService socket the same way Aspire wires it at runtime, so HttpReservationsClient's
/// "https+http://apiservice" base address resolves without booting a real AppHost.
/// </summary>
public sealed class ReservationsE2EFixture : IAsyncLifetime
{
    private const string SigningKey = "e2e-fixture-signing-key-do-not-use-in-production-93482";

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
            builder.UseSetting(PlaceholderAuthDefaults.SigningKeyConfigKey, SigningKey);
            builder.UseSetting("ConnectionStrings:misedb", _postgres.GetConnectionString());
        });
        var apiBaseUrl = _apiFactory.Start();

        using (var scope = _apiFactory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ReservationsDbContext>();
            await dbContext.Database.MigrateAsync(TestContext.Current.CancellationToken);
        }

        _webFactory = new KestrelFactory<Program>(builder =>
        {
            builder.UseSetting(PlaceholderAuthDefaults.SigningKeyConfigKey, SigningKey);
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
public sealed class ReservationsE2ECollection : ICollectionFixture<ReservationsE2EFixture>
{
    public const string Name = "ReservationsE2E";
}
