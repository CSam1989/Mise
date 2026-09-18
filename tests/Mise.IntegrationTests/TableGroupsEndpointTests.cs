using System.Net.Http.Json;
using System.Text.Json;

namespace Mise.IntegrationTests;

[Trait("Category", "Integration")]
[Collection(MiseApiCollection.Name)]
public class TableGroupsEndpointTests(MiseApiFixture fixture)
{
    private static CancellationToken CT => TestContext.Current.CancellationToken;

    private HttpClient ManagerClient => fixture.CreateAuthenticatedClient(role: "Manager");

    private async Task<Guid> CreateSectionAsync()
    {
        var response = await ManagerClient.PostAsJsonAsync(
            "/api/sections", new { OperationId = Guid.NewGuid(), Name = $"Section-{Guid.NewGuid():N}", DisplayOrder = 0 }, CT);
        var json = await response.Content.ReadFromJsonAsync<JsonDocument>(CT);
        return json!.RootElement.GetProperty("id").GetGuid();
    }

    private async Task<Guid> CreateTableAsync(bool isCombinable = true)
    {
        var sectionId = await CreateSectionAsync();
        var response = await ManagerClient.PostAsJsonAsync(
            "/api/tables",
            new
            {
                OperationId = Guid.NewGuid(),
                SectionId = sectionId,
                Name = $"T-{Guid.NewGuid():N}",
                MinCapacity = 2,
                MaxCapacity = 4,
                IsCombinable = isCombinable,
                PositionX = (double?)null,
                PositionY = (double?)null,
            },
            CT);
        var json = await response.Content.ReadFromJsonAsync<JsonDocument>(CT);
        return json!.RootElement.GetProperty("id").GetGuid();
    }

    private static object ValidBody(Guid? operationId = null, string? name = null, params Guid[] tableIds) => new
    {
        OperationId = operationId ?? Guid.NewGuid(),
        Name = name ?? $"Group-{Guid.NewGuid():N}",
        TableIds = tableIds,
    };

    [Fact]
    public async Task PostTableGroup_ValidBody_Returns201()
    {
        var tableA = await CreateTableAsync();
        var tableB = await CreateTableAsync();

        var response = await ManagerClient.PostAsJsonAsync("/api/table-groups", ValidBody(name: "T1+T2", tableIds: [tableA, tableB]), CT);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var json = await response.Content.ReadFromJsonAsync<JsonDocument>(CT);
        json!.RootElement.GetProperty("name").GetString().Should().Be("T1+T2");
        json.RootElement.GetProperty("isActive").GetBoolean().Should().BeTrue();
        json.RootElement.GetProperty("tableIds").EnumerateArray().Select(e => e.GetGuid()).Should().BeEquivalentTo([tableA, tableB]);
    }

    [Fact]
    public async Task PostTableGroup_OnlyOneTableId_Returns400WithFieldError()
    {
        var tableA = await CreateTableAsync();

        var response = await ManagerClient.PostAsJsonAsync("/api/table-groups", ValidBody(tableIds: [tableA]), CT);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var json = await response.Content.ReadFromJsonAsync<JsonDocument>(CT);
        json!.RootElement.GetProperty("errors").GetProperty("TableIds")[0].GetString()
            .Should().Be("TableIds must name at least two distinct tables.");
    }

    [Fact]
    public async Task PostTableGroup_TableNotCombinable_Returns400WithFieldError()
    {
        var tableA = await CreateTableAsync(isCombinable: true);
        var tableB = await CreateTableAsync(isCombinable: false);

        var response = await ManagerClient.PostAsJsonAsync("/api/table-groups", ValidBody(tableIds: [tableA, tableB]), CT);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var json = await response.Content.ReadFromJsonAsync<JsonDocument>(CT);
        json!.RootElement.GetProperty("errors").GetProperty("TableIds")[0].GetString().Should().Contain("not marked combinable");
    }

    [Fact]
    public async Task PostTableGroup_TableAlreadyInAnActiveGroup_Returns400WithFieldError()
    {
        var tableA = await CreateTableAsync();
        var tableB = await CreateTableAsync();
        var tableC = await CreateTableAsync();
        await ManagerClient.PostAsJsonAsync("/api/table-groups", ValidBody(tableIds: [tableA, tableB]), CT);

        var response = await ManagerClient.PostAsJsonAsync("/api/table-groups", ValidBody(tableIds: [tableA, tableC]), CT);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var json = await response.Content.ReadFromJsonAsync<JsonDocument>(CT);
        json!.RootElement.GetProperty("errors").GetProperty("TableIds")[0].GetString().Should().Contain("already belongs to another active table group");
    }

    [Fact]
    public async Task PostTableGroup_CallerIsFloorStaff_Returns403()
    {
        var tableA = await CreateTableAsync();
        var tableB = await CreateTableAsync();
        var floorStaffClient = fixture.CreateAuthenticatedClient(Guid.NewGuid(), role: "FloorStaff");

        var response = await floorStaffClient.PostAsJsonAsync("/api/table-groups", ValidBody(tableIds: [tableA, tableB]), CT);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task PostTableGroup_Unauthenticated_Returns401()
    {
        var tableA = await CreateTableAsync();
        var tableB = await CreateTableAsync();

        var response = await fixture.CreateClient().PostAsJsonAsync("/api/table-groups", ValidBody(tableIds: [tableA, tableB]), CT);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task PostTableGroup_SameOperationIdTwice_CreatesExactlyOneGroup()
    {
        var tableA = await CreateTableAsync();
        var tableB = await CreateTableAsync();
        var body = ValidBody(operationId: Guid.NewGuid(), tableIds: [tableA, tableB]);

        var first = await ManagerClient.PostAsJsonAsync("/api/table-groups", body, CT);
        var second = await ManagerClient.PostAsJsonAsync("/api/table-groups", body, CT);

        first.StatusCode.Should().Be(HttpStatusCode.Created);
        second.StatusCode.Should().Be(HttpStatusCode.Created);
        var firstJson = await first.Content.ReadFromJsonAsync<JsonDocument>(CT);
        var secondJson = await second.Content.ReadFromJsonAsync<JsonDocument>(CT);
        secondJson!.RootElement.GetProperty("id").GetGuid().Should().Be(firstJson!.RootElement.GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task PostTableGroup_ValidBody_WritesExactlyOneMatchingAuditLogEntry()
    {
        var tableA = await CreateTableAsync();
        var tableB = await CreateTableAsync();
        var response = await ManagerClient.PostAsJsonAsync("/api/table-groups", ValidBody(tableIds: [tableA, tableB]), CT);
        var json = await response.Content.ReadFromJsonAsync<JsonDocument>(CT);
        var groupId = json!.RootElement.GetProperty("id").GetGuid();

        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(CT);
        await using var command = connection.CreateCommand();
        command.CommandText = "select count(*) from shared.audit_log_entry where entity_id = @id and entity_type = 'TableGroup' and action = 'Created'";
        command.Parameters.AddWithValue("id", groupId);
        (await command.ExecuteScalarAsync(CT)).Should().Be(1L);
    }
}
