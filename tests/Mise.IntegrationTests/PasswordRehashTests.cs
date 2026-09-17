using System.Net.Http.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Mise.Modules.StaffIdentity.Domain;
using Mise.Modules.StaffIdentity.Infrastructure.Persistence;

namespace Mise.IntegrationTests;

/// <summary>
/// Proves docs/plan.md's non-deferrable guarantee: whichever password hasher is chosen
/// later (Argon2id vs. staying on Identity's default), the rehash-on-login migration path
/// must already work. It does, transparently, via ASP.NET Core Identity's own
/// UserManager.CheckPasswordAsync — this test is what makes that a proven fact rather than an
/// assumption. A throwaway DI container (real Identity/EF pipeline, only the password hasher
/// swapped to an older compatibility mode) creates the user, so the row shape always matches
/// whatever Identity's own model actually is — no hand-written INSERT duplicating that
/// knowledge and risking drift from it.
/// </summary>
[Trait("Category", "Integration")]
[Collection(MiseApiCollection.Name)]
public class PasswordRehashTests(MiseApiFixture fixture)
{
    private static CancellationToken CT => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Login_PasswordHashedWithOlderCompatibilityMode_StillSucceedsAndTransparentlyRehashes()
    {
        var username = $"rehash-test-{Guid.NewGuid():N}";
        const string password = "correct-horse-battery";

        var services = new ServiceCollection();
        services.AddDbContext<StaffIdentityDbContext>(options =>
            options.UseNpgsql(fixture.ConnectionString, npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", "staff_identity")));
        services.AddIdentityCore<StaffIdentityUser>(options =>
            {
                options.Password.RequireDigit = false;
                options.Password.RequireLowercase = false;
                options.Password.RequireUppercase = false;
                options.Password.RequireNonAlphanumeric = false;
                options.Password.RequiredLength = 1;
            })
            .AddEntityFrameworkStores<StaffIdentityDbContext>();
        // The one deliberate difference from the app's own configuration (StaffIdentityPersistenceServiceCollectionExtensions):
        // an older hash format, so this test can prove CheckPasswordAsync still accepts it.
        services.Configure<PasswordHasherOptions>(options => options.CompatibilityMode = PasswordHasherCompatibilityMode.IdentityV2);

        var staffId = Guid.NewGuid();
        await using (var provider = services.BuildServiceProvider())
        await using (var scope = provider.CreateAsyncScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<StaffIdentityUser>>();

            var createResult = await userManager.CreateAsync(new StaffIdentityUser { Id = staffId, UserName = username }, password);
            createResult.Succeeded.Should().BeTrue(string.Join("; ", createResult.Errors.Select(e => e.Description)));
        }

        // Raw SQL, not the DbSet: StaffIdentityDbContext.StaffUsers is internal.
        await using (var connection = new NpgsqlConnection(fixture.ConnectionString))
        {
            await connection.OpenAsync(CT);
            await using var command = connection.CreateCommand();
            command.CommandText = "insert into staff_identity.staff_user (id, full_name, role, is_active) values (@id, @fullName, @role, true)";
            command.Parameters.AddWithValue("id", staffId);
            command.Parameters.AddWithValue("fullName", "Rehash Test User");
            command.Parameters.AddWithValue("role", nameof(StaffRole.FloorStaff));
            await command.ExecuteNonQueryAsync(CT);
        }

        var originalHash = await ReadPasswordHashAsync(username);

        var response = await fixture.CreateClient().PostAsJsonAsync("/api/auth/login", new { Username = username, Password = password }, CT);
        response.StatusCode.Should().Be(HttpStatusCode.OK,
            because: "CheckPasswordAsync must still verify a hash produced by an older hasher compatibility mode.");

        var currentHash = await ReadPasswordHashAsync(username);
        currentHash.Should().NotBe(originalHash,
            because: "UserManager.CheckPasswordAsync transparently rehashes on a successful verify against an older format — docs/plan.md's non-deferrable rehash-on-login path.");

        var secondLogin = await fixture.CreateClient().PostAsJsonAsync("/api/auth/login", new { Username = username, Password = password }, CT);
        secondLogin.StatusCode.Should().Be(HttpStatusCode.OK,
            because: "the rehashed value must itself be a valid, verifiable hash of the same password — not a rehash into something broken.");
    }

    private async Task<string> ReadPasswordHashAsync(string username)
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(CT);
        await using var command = connection.CreateCommand();
        command.CommandText = """select "PasswordHash" from staff_identity."AspNetUsers" where "UserName" = @username""";
        command.Parameters.AddWithValue("username", username);
        return (string)(await command.ExecuteScalarAsync(CT))!;
    }
}
