using System.Net.Http.Json;
using System.Text.Json;

namespace Mise.IntegrationTests;

[Trait("Category", "Integration")]
[Collection(MiseApiCollection.Name)]
public class TablesEndpointTests(MiseApiFixture fixture)
{
    private static CancellationToken CT => TestContext.Current.CancellationToken;

    private HttpClient ManagerClient => fixture.CreateAuthenticatedClient(role: "Manager");

    private async Task<Guid> CreateSectionAsync(string? name = null)
    {
        var response = await ManagerClient.PostAsJsonAsync(
            "/api/sections", new { OperationId = Guid.NewGuid(), Name = name ?? $"Section-{Guid.NewGuid():N}", DisplayOrder = 0 }, CT);
        var json = await response.Content.ReadFromJsonAsync<JsonDocument>(CT);
        return json!.RootElement.GetProperty("id").GetGuid();
    }

    private object ValidBody(
        Guid sectionId, Guid? operationId = null, string? name = null, int minCapacity = 2, int maxCapacity = 4) => new
        {
            OperationId = operationId ?? Guid.NewGuid(),
            SectionId = sectionId,
            Name = name ?? $"T-{Guid.NewGuid():N}",
            MinCapacity = minCapacity,
            MaxCapacity = maxCapacity,
            IsCombinable = false,
            PositionX = (double?)null,
            PositionY = (double?)null,
        };

    [Fact]
    public async Task PostTable_ValidBody_Returns201WithLocationAndETag()
    {
        var sectionId = await CreateSectionAsync();

        var response = await ManagerClient.PostAsJsonAsync("/api/tables", ValidBody(sectionId, name: "T-Create"), CT);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        response.Headers.Location.Should().NotBeNull();
        response.Headers.ETag.Should().NotBeNull(because: "docs/plan.md correction #5 — every mutating Table response carries the current version.");

        var json = await response.Content.ReadFromJsonAsync<JsonDocument>(CT);
        json!.RootElement.GetProperty("name").GetString().Should().Be("T-Create");
        json.RootElement.GetProperty("status").GetString().Should().Be("Available");
        json.RootElement.GetProperty("isActive").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task PostTable_MinCapacityZero_Returns400WithFieldError()
    {
        var sectionId = await CreateSectionAsync();

        var response = await ManagerClient.PostAsJsonAsync("/api/tables", ValidBody(sectionId, minCapacity: 0), CT);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var json = await response.Content.ReadFromJsonAsync<JsonDocument>(CT);
        json!.RootElement.GetProperty("errors").GetProperty("MinCapacity")[0].GetString()
            .Should().Be("MinCapacity must be greater than 0.");
    }

    [Fact]
    public async Task PostTable_SectionIdDoesNotExist_Returns400WithFieldError()
    {
        var response = await ManagerClient.PostAsJsonAsync("/api/tables", ValidBody(Guid.NewGuid()), CT);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            because: "a nonexistent SectionId must be a clean 400, not a raw FK-violation 500.");
        var json = await response.Content.ReadFromJsonAsync<JsonDocument>(CT);
        json!.RootElement.GetProperty("errors").GetProperty("SectionId")[0].GetString()
            .Should().Be("SectionId does not refer to an existing section.");
    }

    [Fact]
    public async Task PostTable_CallerIsFloorStaff_Returns403()
    {
        var sectionId = await CreateSectionAsync();
        var floorStaffClient = fixture.CreateAuthenticatedClient(Guid.NewGuid(), role: "FloorStaff");

        var response = await floorStaffClient.PostAsJsonAsync("/api/tables", ValidBody(sectionId), CT);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task PostTable_Unauthenticated_Returns401()
    {
        var sectionId = await CreateSectionAsync();

        var response = await fixture.CreateClient().PostAsJsonAsync("/api/tables", ValidBody(sectionId), CT);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task PostTable_SameOperationIdTwice_CreatesExactlyOneTable()
    {
        var sectionId = await CreateSectionAsync();
        var body = ValidBody(sectionId, operationId: Guid.NewGuid(), name: "Replay Test Table");

        var first = await ManagerClient.PostAsJsonAsync("/api/tables", body, CT);
        var second = await ManagerClient.PostAsJsonAsync("/api/tables", body, CT);

        first.StatusCode.Should().Be(HttpStatusCode.Created);
        second.StatusCode.Should().Be(HttpStatusCode.Created);
        var firstJson = await first.Content.ReadFromJsonAsync<JsonDocument>(CT);
        var secondJson = await second.Content.ReadFromJsonAsync<JsonDocument>(CT);
        secondJson!.RootElement.GetProperty("id").GetGuid().Should().Be(firstJson!.RootElement.GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task PostTable_ValidBody_WritesExactlyOneMatchingAuditLogEntry()
    {
        var sectionId = await CreateSectionAsync();
        var response = await ManagerClient.PostAsJsonAsync("/api/tables", ValidBody(sectionId, name: "Audit Check Table"), CT);
        var json = await response.Content.ReadFromJsonAsync<JsonDocument>(CT);
        var tableId = json!.RootElement.GetProperty("id").GetGuid();

        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(CT);
        await using var command = connection.CreateCommand();
        command.CommandText =
            "select performed_by_staff_id from shared.audit_log_entry where entity_id = @id and action = 'Created'";
        command.Parameters.AddWithValue("id", tableId);
        await using var reader = await command.ExecuteReaderAsync(CT);

        (await reader.ReadAsync(CT)).Should().BeTrue();
        (await reader.ReadAsync(CT)).Should().BeFalse(because: "exactly one audit entry, not more.");
    }

    [Fact]
    public async Task GetFloorPlan_ReturnsActiveTableWithVersion()
    {
        var sectionId = await CreateSectionAsync();
        var name = $"Floor-Plan-Check-{Guid.NewGuid():N}";
        await ManagerClient.PostAsJsonAsync("/api/tables", ValidBody(sectionId, name: name), CT);

        var response = await fixture.CreateAuthenticatedClient(role: "FloorStaff").GetAsync($"/api/tables/floor-plan?sectionId={sectionId}", CT);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonDocument>(CT);
        var row = json!.RootElement.EnumerateArray().Should().ContainSingle(e => e.GetProperty("name").GetString() == name).Which;
        row.GetProperty("version").GetUInt32().Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task PatchTable_MissingIfMatch_Returns428()
    {
        var sectionId = await CreateSectionAsync();
        var created = await ManagerClient.PostAsJsonAsync("/api/tables", ValidBody(sectionId), CT);
        var tableId = (await created.Content.ReadFromJsonAsync<JsonDocument>(CT))!.RootElement.GetProperty("id").GetGuid();

        var request = new HttpRequestMessage(HttpMethod.Patch, $"/api/tables/{tableId}")
        {
            Content = JsonContent.Create(ValidBody(sectionId, name: "Updated Without ETag")),
        };
        var response = await ManagerClient.SendAsync(request, CT);

        response.StatusCode.Should().Be((HttpStatusCode)428,
            because: "docs/plan.md correction #5 — a missing If-Match header is 428 Precondition Required.");
    }

    [Fact]
    public async Task PatchTable_ValidIfMatch_Returns200AndUpdatesFieldsWithNewETag()
    {
        var sectionId = await CreateSectionAsync();
        var created = await ManagerClient.PostAsJsonAsync("/api/tables", ValidBody(sectionId), CT);
        var createdJson = await created.Content.ReadFromJsonAsync<JsonDocument>(CT);
        var tableId = createdJson!.RootElement.GetProperty("id").GetGuid();
        var etag = created.Headers.ETag!.Tag;

        var request = new HttpRequestMessage(HttpMethod.Patch, $"/api/tables/{tableId}")
        {
            Content = JsonContent.Create(ValidBody(sectionId, name: "Updated With ETag", minCapacity: 5, maxCapacity: 8)),
        };
        request.Headers.TryAddWithoutValidation("If-Match", etag);
        var response = await ManagerClient.SendAsync(request, CT);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.ETag!.Tag.Should().NotBe(etag, because: "a successful update must advance the version.");
        var json = await response.Content.ReadFromJsonAsync<JsonDocument>(CT);
        json!.RootElement.GetProperty("name").GetString().Should().Be("Updated With ETag");
        json.RootElement.GetProperty("maxCapacity").GetInt32().Should().Be(8);
    }

    [Fact]
    public async Task PatchTable_StaleIfMatch_Returns409WithCurrentState()
    {
        var sectionId = await CreateSectionAsync();
        var created = await ManagerClient.PostAsJsonAsync("/api/tables", ValidBody(sectionId, name: "Stale ETag Table"), CT);
        var createdJson = await created.Content.ReadFromJsonAsync<JsonDocument>(CT);
        var tableId = createdJson!.RootElement.GetProperty("id").GetGuid();
        var originalEtag = created.Headers.ETag!.Tag;

        // Advance the version once so the caller's original ETag is now stale.
        var firstUpdate = new HttpRequestMessage(HttpMethod.Patch, $"/api/tables/{tableId}")
        {
            Content = JsonContent.Create(ValidBody(sectionId, name: "First Update")),
        };
        firstUpdate.Headers.TryAddWithoutValidation("If-Match", originalEtag);
        (await ManagerClient.SendAsync(firstUpdate, CT)).StatusCode.Should().Be(HttpStatusCode.OK);

        var staleUpdate = new HttpRequestMessage(HttpMethod.Patch, $"/api/tables/{tableId}")
        {
            Content = JsonContent.Create(ValidBody(sectionId, name: "Second Update Using Stale ETag")),
        };
        staleUpdate.Headers.TryAddWithoutValidation("If-Match", originalEtag);
        var response = await ManagerClient.SendAsync(staleUpdate, CT);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict,
            because: "docs/plan.md correction #5 — a stale If-Match must be rejected with 409, never a silent overwrite (NFR-03).");
        response.Headers.ETag.Should().BeNull(
            because: "ASP.NET Core's ExceptionHandlerMiddleware strips any ETag header from an error response (CLAUDE.md's documented gotcha) — the current version travels in the body instead.");
        var json = await response.Content.ReadFromJsonAsync<JsonDocument>(CT);
        json!.RootElement.GetProperty("currentVersion").GetString().Should().NotBeNullOrEmpty(
            because: "the caller needs the fresh version to retry without a separate GET.");
        json.RootElement.GetProperty("currentState").GetProperty("name").GetString().Should().Be("First Update");
    }

    [Fact]
    public async Task PatchTableDeactivate_MissingIfMatch_Returns428()
    {
        var sectionId = await CreateSectionAsync();
        var created = await ManagerClient.PostAsJsonAsync("/api/tables", ValidBody(sectionId), CT);
        var tableId = (await created.Content.ReadFromJsonAsync<JsonDocument>(CT))!.RootElement.GetProperty("id").GetGuid();

        var request = new HttpRequestMessage(HttpMethod.Patch, $"/api/tables/{tableId}/deactivate")
        {
            Content = JsonContent.Create(new { OperationId = Guid.NewGuid() }),
        };
        var response = await ManagerClient.SendAsync(request, CT);

        response.StatusCode.Should().Be((HttpStatusCode)428);
    }

    [Fact]
    public async Task PatchTableDeactivate_ValidIfMatch_Returns200AndIsActiveFalse()
    {
        var sectionId = await CreateSectionAsync();
        var created = await ManagerClient.PostAsJsonAsync("/api/tables", ValidBody(sectionId, name: "To Deactivate"), CT);
        var createdJson = await created.Content.ReadFromJsonAsync<JsonDocument>(CT);
        var tableId = createdJson!.RootElement.GetProperty("id").GetGuid();
        var etag = created.Headers.ETag!.Tag;

        var request = new HttpRequestMessage(HttpMethod.Patch, $"/api/tables/{tableId}/deactivate")
        {
            Content = JsonContent.Create(new { OperationId = Guid.NewGuid() }),
        };
        request.Headers.TryAddWithoutValidation("If-Match", etag);
        var response = await ManagerClient.SendAsync(request, CT);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonDocument>(CT);
        json!.RootElement.GetProperty("isActive").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task PatchTableDeactivate_StatusOccupied_Returns409()
    {
        var sectionId = await CreateSectionAsync();
        var created = await ManagerClient.PostAsJsonAsync("/api/tables", ValidBody(sectionId, name: "Occupied Table"), CT);
        var createdJson = await created.Content.ReadFromJsonAsync<JsonDocument>(CT);
        var tableId = createdJson!.RootElement.GetProperty("id").GetGuid();
        var createEtag = created.Headers.ETag!.Tag;

        // Phase 7's real PATCH /api/tables/{id}/status (FR-06) now exists — no more manufacturing
        // the Occupied state via raw SQL the way this test had to before that endpoint existed.
        var statusRequest = new HttpRequestMessage(HttpMethod.Patch, $"/api/tables/{tableId}/status")
        {
            Content = JsonContent.Create(new { OperationId = Guid.NewGuid(), Status = "Occupied" }),
        };
        statusRequest.Headers.TryAddWithoutValidation("If-Match", createEtag);
        var statusResponse = await ManagerClient.SendAsync(statusRequest, CT);
        var currentEtag = statusResponse.Headers.ETag!.Tag;

        var request = new HttpRequestMessage(HttpMethod.Patch, $"/api/tables/{tableId}/deactivate")
        {
            Content = JsonContent.Create(new { OperationId = Guid.NewGuid() }),
        };
        request.Headers.TryAddWithoutValidation("If-Match", currentEtag);
        var response = await ManagerClient.SendAsync(request, CT);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict,
            because: "docs/plan.md correction #13 — deactivating an Occupied table is blocked with a clear reason, not a silent failure (US-04's edge case).");
        var rawBody = await response.Content.ReadAsStringAsync(CT);
        rawBody.Should().Contain("Occupied");
    }

    // --- Change status (FR-06) ----------------------------------------------------------------

    private async Task<(Guid Id, string ETag)> CreateTableWithETagAsync()
    {
        var sectionId = await CreateSectionAsync();
        var response = await ManagerClient.PostAsJsonAsync("/api/tables", ValidBody(sectionId), CT);
        var json = await response.Content.ReadFromJsonAsync<JsonDocument>(CT);
        return (json!.RootElement.GetProperty("id").GetGuid(), response.Headers.ETag!.Tag);
    }

    [Fact]
    public async Task PatchTableStatus_MissingIfMatch_Returns428()
    {
        var (id, _) = await CreateTableWithETagAsync();

        var response = await ManagerClient.PatchAsJsonAsync($"/api/tables/{id}/status", new { OperationId = Guid.NewGuid(), Status = "Occupied" }, CT);

        response.StatusCode.Should().Be((HttpStatusCode)428);
    }

    [Fact]
    public async Task PatchTableStatus_TableDoesNotExist_Returns404()
    {
        var request = new HttpRequestMessage(HttpMethod.Patch, $"/api/tables/{Guid.NewGuid()}/status")
        {
            Content = JsonContent.Create(new { OperationId = Guid.NewGuid(), Status = "Occupied" }),
        };
        request.Headers.TryAddWithoutValidation("If-Match", "\"1\"");
        var response = await ManagerClient.SendAsync(request, CT);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task PatchTableStatus_InvalidStatusValue_Returns400WithFieldError()
    {
        var (id, etag) = await CreateTableWithETagAsync();

        var request = new HttpRequestMessage(HttpMethod.Patch, $"/api/tables/{id}/status")
        {
            Content = JsonContent.Create(new { OperationId = Guid.NewGuid(), Status = "OnFire" }),
        };
        request.Headers.TryAddWithoutValidation("If-Match", etag);
        var response = await ManagerClient.SendAsync(request, CT);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var json = await response.Content.ReadFromJsonAsync<JsonDocument>(CT);
        json!.RootElement.GetProperty("errors").GetProperty("Status")[0].GetString()
            .Should().Be("Status must be one of: Available, Reserved, Occupied, NeedsCleaning, Blocked.");
    }

    [Theory]
    [InlineData("Occupied")]
    [InlineData("NeedsCleaning")]
    [InlineData("Blocked")]
    [InlineData("Reserved")]
    public async Task PatchTableStatus_ValidStatusDifferentFromCurrent_Returns200AndUpdatesStatus(string status)
    {
        // A freshly created table already starts Available (excluded from this theory — setting
        // it to the value it already has is a genuine no-op at the SQL level, since EF Core
        // skips emitting an UPDATE for an unchanged property, so xmin correctly does not advance
        // for that case; PatchTableStatus_SameStatusAsCurrent_Returns200WithUnchangedETag below
        // covers it instead).
        var (id, etag) = await CreateTableWithETagAsync();

        var request = new HttpRequestMessage(HttpMethod.Patch, $"/api/tables/{id}/status")
        {
            Content = JsonContent.Create(new { OperationId = Guid.NewGuid(), Status = status }),
        };
        request.Headers.TryAddWithoutValidation("If-Match", etag);
        var response = await ManagerClient.SendAsync(request, CT);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.ETag!.Tag.Should().NotBe(etag, because: "a successful status change to a genuinely different value must advance the version.");
        var json = await response.Content.ReadFromJsonAsync<JsonDocument>(CT);
        json!.RootElement.GetProperty("status").GetString().Should().Be(status);
    }

    [Fact]
    public async Task PatchTableStatus_SameStatusAsCurrent_Returns200WithUnchangedETag()
    {
        var (id, etag) = await CreateTableWithETagAsync();

        var request = new HttpRequestMessage(HttpMethod.Patch, $"/api/tables/{id}/status")
        {
            Content = JsonContent.Create(new { OperationId = Guid.NewGuid(), Status = "Available" }),
        };
        request.Headers.TryAddWithoutValidation("If-Match", etag);
        var response = await ManagerClient.SendAsync(request, CT);

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            because: "FR-06 has no transition guard, including setting a status to the value it already has.");
        response.Headers.ETag!.Tag.Should().Be(etag,
            because: "EF Core skips emitting an UPDATE for a property whose value didn't change, so xmin does not advance.");
    }

    [Fact]
    public async Task PatchTableStatus_ValidIfMatch_WritesExactlyOneMatchingAuditLogEntry()
    {
        var (id, etag) = await CreateTableWithETagAsync();
        var request = new HttpRequestMessage(HttpMethod.Patch, $"/api/tables/{id}/status")
        {
            Content = JsonContent.Create(new { OperationId = Guid.NewGuid(), Status = "NeedsCleaning" }),
        };
        request.Headers.TryAddWithoutValidation("If-Match", etag);
        await ManagerClient.SendAsync(request, CT);

        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(CT);
        await using var command = connection.CreateCommand();
        command.CommandText = "select count(*) from shared.audit_log_entry where entity_id = @id and action = 'StatusChanged'";
        command.Parameters.AddWithValue("id", id);
        (await command.ExecuteScalarAsync(CT)).Should().Be(1L);
    }

    [Fact]
    public async Task PatchTableStatus_SameOperationIdTwice_ChangesOnlyOnce()
    {
        var (id, etag) = await CreateTableWithETagAsync();
        var body = new { OperationId = Guid.NewGuid(), Status = "Blocked" };

        var request1 = new HttpRequestMessage(HttpMethod.Patch, $"/api/tables/{id}/status") { Content = JsonContent.Create(body) };
        request1.Headers.TryAddWithoutValidation("If-Match", etag);
        var first = await ManagerClient.SendAsync(request1, CT);

        var request2 = new HttpRequestMessage(HttpMethod.Patch, $"/api/tables/{id}/status") { Content = JsonContent.Create(body) };
        request2.Headers.TryAddWithoutValidation("If-Match", etag);
        var second = await ManagerClient.SendAsync(request2, CT);

        first.StatusCode.Should().Be(HttpStatusCode.OK);
        second.StatusCode.Should().Be(HttpStatusCode.OK, because: "a replayed OperationId must still succeed — idempotent, not rejected.");

        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(CT);
        await using var command = connection.CreateCommand();
        command.CommandText = "select count(*) from shared.audit_log_entry where entity_id = @id and action = 'StatusChanged'";
        command.Parameters.AddWithValue("id", id);
        (await command.ExecuteScalarAsync(CT)).Should().Be(1L, because: "the replay must not write a second audit entry for the same effect.");
    }

    [Fact]
    public async Task PatchTableStatus_CallerIsFloorStaff_Returns200()
    {
        var (id, etag) = await CreateTableWithETagAsync();
        var floorStaffClient = fixture.CreateAuthenticatedClient(role: "FloorStaff");
        var request = new HttpRequestMessage(HttpMethod.Patch, $"/api/tables/{id}/status")
        {
            Content = JsonContent.Create(new { OperationId = Guid.NewGuid(), Status = "Occupied" }),
        };
        request.Headers.TryAddWithoutValidation("If-Match", etag);

        var response = await floorStaffClient.SendAsync(request, CT);

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            because: "FR-06 is deliberately FloorStaff-or-Manager, unlike Create/Update/Deactivate's Manager-only configuration actions.");
    }

    [Fact]
    public async Task PatchTableStatus_Unauthenticated_Returns401()
    {
        var (id, etag) = await CreateTableWithETagAsync();
        var request = new HttpRequestMessage(HttpMethod.Patch, $"/api/tables/{id}/status")
        {
            Content = JsonContent.Create(new { OperationId = Guid.NewGuid(), Status = "Occupied" }),
        };
        request.Headers.TryAddWithoutValidation("If-Match", etag);

        var response = await fixture.CreateClient().SendAsync(request, CT);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
