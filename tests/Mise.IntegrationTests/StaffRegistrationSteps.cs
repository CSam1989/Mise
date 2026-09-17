using System.Net.Http.Json;
using System.Text.Json;

namespace Mise.IntegrationTests;

/// <summary>Shared by AuthEndpointTests and StaffEndpointTests, both of which need "a
/// freshly registered staff account" isolated from every other test — MiseApiFixture's
/// database is real and committed (no per-test rollback), so counting rows against a shared
/// account (e.g. the seed Manager) across many tests would be flaky by construction.</summary>
internal static class StaffRegistrationSteps
{
    public static async Task<RegisteredStaff> RegisterFreshFloorStaffAsync(MiseApiFixture fixture, CancellationToken cancellationToken)
    {
        var managerClient = fixture.CreateAuthenticatedClient(fixture.SeedManagerStaffId, role: "Manager");
        var username = $"staff-{Guid.NewGuid():N}";
        const string password = "correct-horse-battery";

        var response = await managerClient.PostAsJsonAsync("/api/staff", new
        {
            OperationId = Guid.NewGuid(),
            Username = username,
            Password = password,
            FullName = "Fresh Test Staff",
            Role = "FloorStaff",
        }, cancellationToken);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken);
        var staffUserId = json!.RootElement.GetProperty("id").GetGuid();

        return new RegisteredStaff(staffUserId, username, password);
    }
}

internal sealed record RegisteredStaff(Guid StaffUserId, string Username, string Password);
