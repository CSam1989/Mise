using System.Net.Http.Json;
using System.Text.Json;

namespace Mise.IntegrationTests;

[Trait("Category", "Integration")]
[Collection(MiseApiCollection.Name)]
public class AuthEndpointTests(MiseApiFixture fixture)
{
    private static CancellationToken CT => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Login_ValidCredentials_Returns200WithToken()
    {
        var response = await fixture.CreateClient().PostAsJsonAsync("/api/auth/login",
            new { Username = MiseApiFixture.SeedManagerUsername, Password = MiseApiFixture.SeedManagerPassword }, CT);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadFromJsonAsync<JsonDocument>(CT);
        json!.RootElement.GetProperty("token").GetString().Should().NotBeNullOrEmpty();
        json.RootElement.GetProperty("role").GetString().Should().Be("Manager");
        json.RootElement.GetProperty("fullName").GetString().Should().Be(MiseApiFixture.SeedManagerFullName);
    }

    [Fact]
    public async Task Login_WrongPassword_Returns401()
    {
        var response = await fixture.CreateClient().PostAsJsonAsync("/api/auth/login",
            new { Username = MiseApiFixture.SeedManagerUsername, Password = "wrong-password" }, CT);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Login_UnknownUsername_Returns401()
    {
        var response = await fixture.CreateClient().PostAsJsonAsync("/api/auth/login",
            new { Username = $"nobody-{Guid.NewGuid():N}", Password = "irrelevant-password" }, CT);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            because: "unknown username and wrong password must be indistinguishable to the caller.");
    }

    [Fact]
    public async Task Login_EmptyUsername_Returns400WithFieldError()
    {
        var response = await fixture.CreateClient().PostAsJsonAsync(
            "/api/auth/login", new { Username = "", Password = "irrelevant" }, CT);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var json = await response.Content.ReadFromJsonAsync<JsonDocument>(CT);
        json!.RootElement.GetProperty("errors").GetProperty("Username")[0].GetString().Should().Be("Username is required.");
    }

    [Fact]
    public async Task Login_ValidCredentials_WritesExactlyOneMatchingSignedInAuditEntry()
    {
        var staff = await StaffRegistrationSteps.RegisterFreshFloorStaffAsync(fixture, CT);

        var response = await fixture.CreateClient().PostAsJsonAsync(
            "/api/auth/login", new { staff.Username, staff.Password }, CT);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(CT);
        await using var command = connection.CreateCommand();
        command.CommandText =
            "select performed_by_staff_id from shared.audit_log_entry where entity_id = @id and action = 'SignedIn'";
        command.Parameters.AddWithValue("id", staff.StaffUserId);
        await using var reader = await command.ExecuteReaderAsync(CT);

        (await reader.ReadAsync(CT)).Should().BeTrue(because: "a successful login must write exactly one matching audit entry.");
        reader.GetGuid(0).Should().Be(staff.StaffUserId);
        (await reader.ReadAsync(CT)).Should().BeFalse(because: "exactly one audit entry, not more.");
    }
}
