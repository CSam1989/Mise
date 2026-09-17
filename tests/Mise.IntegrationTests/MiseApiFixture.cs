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
using Mise.Modules.StaffIdentity.Infrastructure;
using Mise.Modules.StaffIdentity.Infrastructure.Persistence;
using Mise.Modules.Tables.Infrastructure.Persistence;
using Mise.ServiceDefaults;
using Testcontainers.PostgreSql;

namespace Mise.IntegrationTests;

/// <summary>
/// Wraps Mise.ApiService with its own real Testcontainers Postgres — a separate container
/// from PostgresContainerFixture's (that one backs only the ADO.NET-only proof harnesses
/// above). Named for the host it boots, not any one module: as of Phase 3 the composition
/// root wires up both Reservations and StaffIdentity, so both DbContexts need migrating here
/// (it was ReservationsApiFixture through Phase 2, when Reservations was the only module).
/// Also seeds one known Manager account (<see cref="SeedManagerUsername"/>/
/// <see cref="SeedManagerPassword"/>) so real /api/auth/login tests have something to log
/// into — most other endpoint tests mint a token directly via <see cref="MintTestToken"/>
/// instead, the same shortcut Phase 2 used before a login endpoint existed at all.
/// </summary>
public sealed class MiseApiFixture : IAsyncLifetime
{
    public const string SeedManagerUsername = "test-manager";
    public const string SeedManagerPassword = "Test-Manager-Password1";
    public const string SeedManagerFullName = "Test Manager";

    private PostgreSqlContainer? _postgres;
    private WebApplicationFactory<ApiService::Program>? _factory;

    public string ConnectionString => _postgres?.GetConnectionString()
        ?? throw new InvalidOperationException("Container has not been started yet.");

    public Guid SeedManagerStaffId { get; private set; }

    public async ValueTask InitializeAsync()
    {
        _postgres = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await _postgres.StartAsync();

        _factory = new CustomWebApplicationFactory().WithWebHostBuilder(builder =>
            builder.UseSetting("ConnectionStrings:misedb", _postgres.GetConnectionString()));

        using var scope = _factory.Services.CreateScope();

        var reservationsDb = scope.ServiceProvider.GetRequiredService<ReservationsDbContext>();
        await reservationsDb.Database.MigrateAsync(TestContext.Current.CancellationToken);

        var staffIdentityDb = scope.ServiceProvider.GetRequiredService<StaffIdentityDbContext>();
        await staffIdentityDb.Database.MigrateAsync(TestContext.Current.CancellationToken);

        var tablesDb = scope.ServiceProvider.GetRequiredService<TablesDbContext>();
        await tablesDb.Database.MigrateAsync(TestContext.Current.CancellationToken);

        var seeder = scope.ServiceProvider.GetRequiredService<StaffIdentitySeeder>();
        await seeder.EnsureManagerExistsAsync(
            SeedManagerUsername, SeedManagerPassword, SeedManagerFullName, TestContext.Current.CancellationToken);

        // Raw SQL, not the DbSet: StaffIdentityDbContext.StaffUsers is internal (same
        // encapsulation ReservationsDbContext's own DbSets already have) — every other
        // endpoint test in this project reads shared.audit_log_entry/processed_operation the
        // same way, for the same reason.
        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "select id from staff_identity.staff_user where full_name = @fullName";
        command.Parameters.AddWithValue("fullName", SeedManagerFullName);
        SeedManagerStaffId = (Guid)(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken))!;
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

    public HttpClient CreateAuthenticatedClient(Guid? staffId = null, string role = "Manager")
    {
        var client = _factory!.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", MintTestToken(staffId ?? DefaultStaffId, role: role));
        return client;
    }

    /// <summary>The staff id CreateAuthenticatedClient() uses when no specific id is given —
    /// tests asserting on "who performed this" (e.g. an audit entry) use this constant
    /// directly rather than an opaque name string, matching CreateReservationCommand's real
    /// PerformedByStaffId shape (Phase 3).</summary>
    public static readonly Guid DefaultStaffId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    public static string MintTestToken(Guid staffId, string fullName = "integration-test", string role = "Manager")
    {
        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(CustomWebApplicationFactory.TestSigningKey)),
            SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: JwtAuthDefaults.Issuer,
            audience: JwtAuthDefaults.Audience,
            claims:
            [
                new Claim(ClaimTypes.NameIdentifier, staffId.ToString()),
                new Claim(ClaimTypes.Name, fullName),
                new Claim(ClaimTypes.Role, role),
            ],
            expires: DateTime.UtcNow.AddMinutes(5),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}

[CollectionDefinition(Name)]
public sealed class MiseApiCollection : ICollectionFixture<MiseApiFixture>
{
    public const string Name = "MiseApi";
}
