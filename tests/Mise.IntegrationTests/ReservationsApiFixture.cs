extern alias ApiService;
using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Mise.Modules.Reservations.Infrastructure.Persistence;
using Mise.ServiceDefaults;
using Testcontainers.PostgreSql;

namespace Mise.IntegrationTests;

/// <summary>
/// Wraps Mise.ApiService with its own real Testcontainers Postgres — a separate container
/// from PostgresContainerFixture's (that one backs only the ADO.NET-only proof harnesses
/// above), needed because Phase 2 is the first module with a DbContext and real endpoints to
/// exercise. Migrations run once per test run, mirroring what Mise.MigrationService does for
/// `aspire run` (this fixture does not exercise MigrationService itself).
/// </summary>
public sealed class ReservationsApiFixture : IAsyncLifetime
{
    private PostgreSqlContainer? _postgres;
    private WebApplicationFactory<ApiService::Program>? _factory;

    public string ConnectionString => _postgres?.GetConnectionString()
        ?? throw new InvalidOperationException("Container has not been started yet.");

    public async ValueTask InitializeAsync()
    {
        _postgres = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await _postgres.StartAsync();

        _factory = new CustomWebApplicationFactory().WithWebHostBuilder(builder =>
            builder.UseSetting("ConnectionStrings:misedb", _postgres.GetConnectionString()));

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ReservationsDbContext>();
        await dbContext.Database.MigrateAsync(TestContext.Current.CancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (_factory is not null)
        {
            await _factory.DisposeAsync();
        }

        if (_postgres is not null)
        {
            await _postgres.DisposeAsync();
        }
    }

    public HttpClient CreateClient() => _factory!.CreateClient();

    public HttpClient CreateAuthenticatedClient()
    {
        var client = _factory!.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", MintTestToken());
        return client;
    }

    public static string MintTestToken()
    {
        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(CustomWebApplicationFactory.TestSigningKey)),
            SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: PlaceholderAuthDefaults.Issuer,
            audience: PlaceholderAuthDefaults.Audience,
            claims: [new Claim(ClaimTypes.Name, "integration-test")],
            expires: DateTime.UtcNow.AddMinutes(5),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}

[CollectionDefinition(Name)]
public sealed class ReservationsApiCollection : ICollectionFixture<ReservationsApiFixture>
{
    public const string Name = "ReservationsApi";
}
