using System.Net.Http.Json;
using System.Text.Json;

namespace Mise.IntegrationTests;

[Trait("Category", "Integration")]
[Collection(MiseApiCollection.Name)]
public class StaffEndpointTests(MiseApiFixture fixture)
{
    private static CancellationToken CT => TestContext.Current.CancellationToken;

    private HttpClient ManagerClient => fixture.CreateAuthenticatedClient(fixture.SeedManagerStaffId, role: "Manager");

    private static object ValidBody(
        Guid? operationId = null, string? username = null, string password = "correct-horse-battery", string role = "FloorStaff") => new
        {
            OperationId = operationId ?? Guid.NewGuid(),
            Username = username ?? $"staff-{Guid.NewGuid():N}",
            Password = password,
            FullName = "New Staff Member",
            Role = role,
        };

    [Fact]
    public async Task PostStaff_ValidBody_Returns201WithLocation()
    {
        var response = await ManagerClient.PostAsJsonAsync("/api/staff", ValidBody(), CT);

        response.StatusCode.Should().Be(HttpStatusCode.Created, because: "a valid staff registration must be accepted.");
        response.Headers.Location.Should().NotBeNull();

        var json = await response.Content.ReadFromJsonAsync<JsonDocument>(CT);
        json!.RootElement.GetProperty("role").GetString().Should().Be("FloorStaff");
    }

    [Fact]
    public async Task PostStaff_PasswordTooShort_Returns400WithFieldError()
    {
        var response = await ManagerClient.PostAsJsonAsync("/api/staff", ValidBody(password: "short"), CT);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var json = await response.Content.ReadFromJsonAsync<JsonDocument>(CT);
        json!.RootElement.GetProperty("errors").GetProperty("Password")[0].GetString()
            .Should().Be("Password must be at least 8 characters.");
    }

    [Fact]
    public async Task PostStaff_InvalidRoleString_Returns400WithFieldError()
    {
        var response = await ManagerClient.PostAsJsonAsync("/api/staff", ValidBody(role: "SuperAdmin"), CT);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var json = await response.Content.ReadFromJsonAsync<JsonDocument>(CT);
        json!.RootElement.GetProperty("errors").GetProperty("Role")[0].GetString()
            .Should().Be("Role must be FloorStaff or Manager.");
    }

    [Fact]
    public async Task PostStaff_UsernameAlreadyTaken_Returns400WithFieldError()
    {
        var username = $"staff-{Guid.NewGuid():N}";
        (await ManagerClient.PostAsJsonAsync("/api/staff", ValidBody(username: username), CT))
            .StatusCode.Should().Be(HttpStatusCode.Created);

        var response = await ManagerClient.PostAsJsonAsync("/api/staff", ValidBody(username: username), CT);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var json = await response.Content.ReadFromJsonAsync<JsonDocument>(CT);
        json!.RootElement.GetProperty("errors").GetProperty("Username")[0].GetString()
            .Should().Be("This username is already taken.");
    }

    [Fact]
    public async Task PostStaff_CallerIsFloorStaff_Returns403()
    {
        var floorStaffClient = fixture.CreateAuthenticatedClient(Guid.NewGuid(), role: "FloorStaff");

        var response = await floorStaffClient.PostAsJsonAsync("/api/staff", ValidBody(), CT);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            because: "the Manager policy must reject a FloorStaff-role caller (CLAUDE.md's permission-boundary test contract).");
    }

    [Fact]
    public async Task PostStaff_Unauthenticated_Returns401()
    {
        var response = await fixture.CreateClient().PostAsJsonAsync("/api/staff", ValidBody(), CT);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            because: "the global fallback authorization policy requires authentication by default.");
    }

    [Fact]
    public async Task PostStaff_SameOperationIdTwice_CreatesExactlyOneStaffUser()
    {
        var body = ValidBody(operationId: Guid.NewGuid());

        var first = await ManagerClient.PostAsJsonAsync("/api/staff", body, CT);
        var second = await ManagerClient.PostAsJsonAsync("/api/staff", body, CT);

        first.StatusCode.Should().Be(HttpStatusCode.Created);
        second.StatusCode.Should().Be(HttpStatusCode.Created,
            because: "a replayed OperationId must still succeed — idempotent, not rejected.");

        var firstJson = await first.Content.ReadFromJsonAsync<JsonDocument>(CT);
        var secondJson = await second.Content.ReadFromJsonAsync<JsonDocument>(CT);
        secondJson!.RootElement.GetProperty("id").GetGuid().Should().Be(
            firstJson!.RootElement.GetProperty("id").GetGuid(),
            because: "replaying the same OperationId must return the same staff user id, not create a second row.");
    }

    [Fact]
    public async Task PostStaff_ValidBody_WritesExactlyOneMatchingAuditLogEntry()
    {
        var response = await ManagerClient.PostAsJsonAsync("/api/staff", ValidBody(), CT);
        var json = await response.Content.ReadFromJsonAsync<JsonDocument>(CT);
        var staffUserId = json!.RootElement.GetProperty("id").GetGuid();

        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(CT);
        await using var command = connection.CreateCommand();
        command.CommandText =
            "select performed_by_staff_id from shared.audit_log_entry where entity_id = @id and action = 'Created'";
        command.Parameters.AddWithValue("id", staffUserId);
        await using var reader = await command.ExecuteReaderAsync(CT);

        (await reader.ReadAsync(CT)).Should().BeTrue(because: "registering a staff member must write exactly one matching audit entry.");
        reader.GetGuid(0).Should().Be(fixture.SeedManagerStaffId,
            because: "PerformedByStaffId must be the Manager who registered the new staff member, not the new staff member themselves.");
        (await reader.ReadAsync(CT)).Should().BeFalse(because: "exactly one audit entry, not more.");
    }
}
