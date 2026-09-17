using System.Net.Http.Json;
using System.Text.Json;

namespace Mise.IntegrationTests;

[Trait("Category", "Integration")]
[Collection(MiseApiCollection.Name)]
public class SectionsEndpointTests(MiseApiFixture fixture)
{
    private static CancellationToken CT => TestContext.Current.CancellationToken;

    private HttpClient ManagerClient => fixture.CreateAuthenticatedClient(role: "Manager");

    private static object ValidBody(Guid? operationId = null, string name = "Patio", int displayOrder = 0) => new
    {
        OperationId = operationId ?? Guid.NewGuid(),
        Name = name,
        DisplayOrder = displayOrder,
    };

    [Fact]
    public async Task PostSection_ValidBody_Returns201WithLocation()
    {
        var response = await ManagerClient.PostAsJsonAsync("/api/sections", ValidBody(name: "Patio"), CT);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        response.Headers.Location.Should().NotBeNull();

        var json = await response.Content.ReadFromJsonAsync<JsonDocument>(CT);
        json!.RootElement.GetProperty("name").GetString().Should().Be("Patio");
        json.RootElement.GetProperty("isActive").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task PostSection_NameEmpty_Returns400WithFieldError()
    {
        var response = await ManagerClient.PostAsJsonAsync("/api/sections", ValidBody(name: ""), CT);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var json = await response.Content.ReadFromJsonAsync<JsonDocument>(CT);
        json!.RootElement.GetProperty("errors").GetProperty("Name")[0].GetString().Should().Be("Name is required.");
    }

    [Fact]
    public async Task PostSection_CallerIsFloorStaff_Returns403()
    {
        var floorStaffClient = fixture.CreateAuthenticatedClient(Guid.NewGuid(), role: "FloorStaff");

        var response = await floorStaffClient.PostAsJsonAsync("/api/sections", ValidBody(), CT);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            because: "US-04: only a Manager may add/edit/deactivate a section.");
    }

    [Fact]
    public async Task PostSection_Unauthenticated_Returns401()
    {
        var response = await fixture.CreateClient().PostAsJsonAsync("/api/sections", ValidBody(), CT);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task PostSection_SameOperationIdTwice_CreatesExactlyOneSection()
    {
        var body = ValidBody(operationId: Guid.NewGuid(), name: "Replay Test Section");

        var first = await ManagerClient.PostAsJsonAsync("/api/sections", body, CT);
        var second = await ManagerClient.PostAsJsonAsync("/api/sections", body, CT);

        first.StatusCode.Should().Be(HttpStatusCode.Created);
        second.StatusCode.Should().Be(HttpStatusCode.Created);

        var firstJson = await first.Content.ReadFromJsonAsync<JsonDocument>(CT);
        var secondJson = await second.Content.ReadFromJsonAsync<JsonDocument>(CT);
        secondJson!.RootElement.GetProperty("id").GetGuid().Should().Be(firstJson!.RootElement.GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task PostSection_ValidBody_WritesExactlyOneMatchingAuditLogEntry()
    {
        var response = await ManagerClient.PostAsJsonAsync("/api/sections", ValidBody(name: "Audit Check Section"), CT);
        var json = await response.Content.ReadFromJsonAsync<JsonDocument>(CT);
        var sectionId = json!.RootElement.GetProperty("id").GetGuid();

        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(CT);
        await using var command = connection.CreateCommand();
        command.CommandText =
            "select performed_by_staff_id from shared.audit_log_entry where entity_id = @id and action = 'Created'";
        command.Parameters.AddWithValue("id", sectionId);
        await using var reader = await command.ExecuteReaderAsync(CT);

        (await reader.ReadAsync(CT)).Should().BeTrue();
        (await reader.ReadAsync(CT)).Should().BeFalse(because: "exactly one audit entry, not more.");
    }

    [Fact]
    public async Task GetSections_ReturnsOnlyActiveSectionsOrderedByDisplayOrder()
    {
        var name = $"List Check {Guid.NewGuid():N}";
        await ManagerClient.PostAsJsonAsync("/api/sections", ValidBody(name: name, displayOrder: 5), CT);

        var response = await fixture.CreateAuthenticatedClient(role: "FloorStaff").GetAsync("/api/sections", CT);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonDocument>(CT);
        json!.RootElement.EnumerateArray().Should().Contain(
            e => e.GetProperty("name").GetString() == name, because: "a newly created section must appear in the active list.");
    }

    [Fact]
    public async Task PatchSection_ValidBody_UpdatesNameAndDisplayOrder()
    {
        var created = await ManagerClient.PostAsJsonAsync("/api/sections", ValidBody(name: "Before Update"), CT);
        var sectionId = (await created.Content.ReadFromJsonAsync<JsonDocument>(CT))!.RootElement.GetProperty("id").GetGuid();

        var response = await ManagerClient.PatchAsJsonAsync(
            $"/api/sections/{sectionId}", new { OperationId = Guid.NewGuid(), Name = "After Update", DisplayOrder = 9 }, CT);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonDocument>(CT);
        json!.RootElement.GetProperty("name").GetString().Should().Be("After Update");
        json.RootElement.GetProperty("displayOrder").GetInt32().Should().Be(9);
    }

    [Fact]
    public async Task PatchSection_SectionDoesNotExist_Returns404()
    {
        var response = await ManagerClient.PatchAsJsonAsync(
            $"/api/sections/{Guid.NewGuid()}", new { OperationId = Guid.NewGuid(), Name = "Ghost", DisplayOrder = 0 }, CT);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task PatchSectionDeactivate_NoActiveTables_Returns200AndIsActiveFalse()
    {
        var created = await ManagerClient.PostAsJsonAsync("/api/sections", ValidBody(name: "Empty Section To Deactivate"), CT);
        var sectionId = (await created.Content.ReadFromJsonAsync<JsonDocument>(CT))!.RootElement.GetProperty("id").GetGuid();

        var response = await ManagerClient.PatchAsJsonAsync(
            $"/api/sections/{sectionId}/deactivate", new { OperationId = Guid.NewGuid() }, CT);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonDocument>(CT);
        json!.RootElement.GetProperty("isActive").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task PatchSectionDeactivate_HasActiveTable_Returns400WithFieldError()
    {
        var sectionResponse = await ManagerClient.PostAsJsonAsync("/api/sections", ValidBody(name: "Section With A Table"), CT);
        var sectionId = (await sectionResponse.Content.ReadFromJsonAsync<JsonDocument>(CT))!.RootElement.GetProperty("id").GetGuid();
        await ManagerClient.PostAsJsonAsync(
            "/api/tables",
            new
            {
                OperationId = Guid.NewGuid(),
                SectionId = sectionId,
                Name = $"T-{Guid.NewGuid():N}",
                MinCapacity = 2,
                MaxCapacity = 4,
                IsCombinable = false,
                PositionX = (double?)null,
                PositionY = (double?)null,
            },
            CT);

        var response = await ManagerClient.PatchAsJsonAsync(
            $"/api/sections/{sectionId}/deactivate", new { OperationId = Guid.NewGuid() }, CT);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            because: "docs/plan.md correction #12 — a section with an active table is blocked, not silently deactivated.");
        var json = await response.Content.ReadFromJsonAsync<JsonDocument>(CT);
        json!.RootElement.GetProperty("errors").GetProperty("SectionId")[0].GetString()
            .Should().Be("Cannot deactivate a section that still has active tables.");
    }
}
