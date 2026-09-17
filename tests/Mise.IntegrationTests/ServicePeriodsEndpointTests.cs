using System.Net.Http.Json;
using System.Text.Json;

namespace Mise.IntegrationTests;

[Trait("Category", "Integration")]
[Collection(MiseApiCollection.Name)]
public class ServicePeriodsEndpointTests(MiseApiFixture fixture)
{
    private static CancellationToken CT => TestContext.Current.CancellationToken;

    private HttpClient ManagerClient => fixture.CreateAuthenticatedClient(role: "Manager");

    private static object ValidBody(
        Guid? operationId = null,
        DateOnly? date = null,
        string label = "Lunch",
        TimeOnly? startTime = null,
        TimeOnly? endTime = null,
        bool endsNextDay = false,
        bool isClosed = false) => new
        {
            OperationId = operationId ?? Guid.NewGuid(),
            Date = date ?? new DateOnly(2026, 10, 1),
            Label = label,
            StartTime = startTime ?? new TimeOnly(12, 0),
            EndTime = endTime ?? new TimeOnly(14, 30),
            EndsNextDay = endsNextDay,
            IsClosed = isClosed,
        };

    [Fact]
    public async Task PostServicePeriod_ValidBody_Returns201WithLocation()
    {
        var response = await ManagerClient.PostAsJsonAsync("/api/service-periods", ValidBody(label: "Lunch"), CT);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        response.Headers.Location.Should().NotBeNull();

        var json = await response.Content.ReadFromJsonAsync<JsonDocument>(CT);
        json!.RootElement.GetProperty("label").GetString().Should().Be("Lunch");
        json.RootElement.GetProperty("isClosed").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task PostServicePeriod_LabelEmpty_Returns400WithFieldError()
    {
        var response = await ManagerClient.PostAsJsonAsync("/api/service-periods", ValidBody(label: ""), CT);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var json = await response.Content.ReadFromJsonAsync<JsonDocument>(CT);
        json!.RootElement.GetProperty("errors").GetProperty("Label")[0].GetString().Should().Be("Label is required.");
    }

    [Fact]
    public async Task PostServicePeriod_SameDayEndTimeNotAfterStartTime_Returns400WithFieldError()
    {
        var response = await ManagerClient.PostAsJsonAsync(
            "/api/service-periods", ValidBody(startTime: new TimeOnly(14, 0), endTime: new TimeOnly(12, 0)), CT);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var json = await response.Content.ReadFromJsonAsync<JsonDocument>(CT);
        json!.RootElement.GetProperty("errors").GetProperty("EndTime")[0].GetString()
            .Should().Be("EndTime must be after StartTime unless the period ends the next day.");
    }

    [Fact]
    public async Task PostServicePeriod_DinnerEndsNextDay_Returns201()
    {
        var response = await ManagerClient.PostAsJsonAsync(
            "/api/service-periods",
            ValidBody(label: "Dinner", startTime: new TimeOnly(18, 0), endTime: new TimeOnly(1, 0), endsNextDay: true),
            CT);

        response.StatusCode.Should().Be(HttpStatusCode.Created,
            because: "docs/plan.md correction #9 — a midnight-crossing Dinner period must be representable.");
    }

    [Fact]
    public async Task PostServicePeriod_CallerIsFloorStaff_Returns403()
    {
        var floorStaffClient = fixture.CreateAuthenticatedClient(Guid.NewGuid(), role: "FloorStaff");

        var response = await floorStaffClient.PostAsJsonAsync("/api/service-periods", ValidBody(), CT);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            because: "FR-08: only a Manager may define service periods or mark days closed.");
    }

    [Fact]
    public async Task PostServicePeriod_Unauthenticated_Returns401()
    {
        var response = await fixture.CreateClient().PostAsJsonAsync("/api/service-periods", ValidBody(), CT);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task PostServicePeriod_SameOperationIdTwice_CreatesExactlyOneServicePeriod()
    {
        var body = ValidBody(operationId: Guid.NewGuid(), label: "Replay Test Period");

        var first = await ManagerClient.PostAsJsonAsync("/api/service-periods", body, CT);
        var second = await ManagerClient.PostAsJsonAsync("/api/service-periods", body, CT);

        first.StatusCode.Should().Be(HttpStatusCode.Created);
        second.StatusCode.Should().Be(HttpStatusCode.Created);

        var firstJson = await first.Content.ReadFromJsonAsync<JsonDocument>(CT);
        var secondJson = await second.Content.ReadFromJsonAsync<JsonDocument>(CT);
        secondJson!.RootElement.GetProperty("id").GetGuid().Should().Be(firstJson!.RootElement.GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task PostServicePeriod_ValidBody_WritesExactlyOneMatchingAuditLogEntry()
    {
        var response = await ManagerClient.PostAsJsonAsync("/api/service-periods", ValidBody(label: "Audit Check Period"), CT);
        var json = await response.Content.ReadFromJsonAsync<JsonDocument>(CT);
        var servicePeriodId = json!.RootElement.GetProperty("id").GetGuid();

        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(CT);
        await using var command = connection.CreateCommand();
        command.CommandText =
            "select performed_by_staff_id from shared.audit_log_entry where entity_id = @id and action = 'Created'";
        command.Parameters.AddWithValue("id", servicePeriodId);
        await using var reader = await command.ExecuteReaderAsync(CT);

        (await reader.ReadAsync(CT)).Should().BeTrue();
        (await reader.ReadAsync(CT)).Should().BeFalse(because: "exactly one audit entry, not more.");
    }

    [Fact]
    public async Task GetServicePeriods_ReturnsPeriodsForDateOrderedByStartTime()
    {
        var date = new DateOnly(2026, 11, 5);
        await ManagerClient.PostAsJsonAsync(
            "/api/service-periods", ValidBody(date: date, label: "Dinner", startTime: new TimeOnly(18, 0), endTime: new TimeOnly(23, 0)), CT);
        await ManagerClient.PostAsJsonAsync(
            "/api/service-periods", ValidBody(date: date, label: "Lunch", startTime: new TimeOnly(12, 0), endTime: new TimeOnly(14, 30)), CT);

        var response = await fixture.CreateAuthenticatedClient(role: "FloorStaff")
            .GetAsync($"/api/service-periods?date={date:yyyy-MM-dd}", CT);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonDocument>(CT);
        var labels = json!.RootElement.EnumerateArray().Select(e => e.GetProperty("label").GetString()).ToArray();
        labels.Should().Equal(["Lunch", "Dinner"], because: "results are ordered by StartTime, not insertion order.");
    }

    [Fact]
    public async Task PatchServicePeriod_ValidBody_UpdatesFields()
    {
        var created = await ManagerClient.PostAsJsonAsync("/api/service-periods", ValidBody(label: "Before Update"), CT);
        var servicePeriodId = (await created.Content.ReadFromJsonAsync<JsonDocument>(CT))!.RootElement.GetProperty("id").GetGuid();

        var response = await ManagerClient.PatchAsJsonAsync(
            $"/api/service-periods/{servicePeriodId}", ValidBody(label: "After Update"), CT);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonDocument>(CT);
        json!.RootElement.GetProperty("label").GetString().Should().Be("After Update");
    }

    [Fact]
    public async Task PatchServicePeriod_DoesNotExist_Returns404()
    {
        var response = await ManagerClient.PatchAsJsonAsync(
            $"/api/service-periods/{Guid.NewGuid()}", ValidBody(label: "Ghost"), CT);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DeleteServicePeriod_Exists_Returns204()
    {
        var created = await ManagerClient.PostAsJsonAsync("/api/service-periods", ValidBody(label: "To Delete"), CT);
        var servicePeriodId = (await created.Content.ReadFromJsonAsync<JsonDocument>(CT))!.RootElement.GetProperty("id").GetGuid();

        var response = await ManagerClient.DeleteAsync($"/api/service-periods/{servicePeriodId}?operationId={Guid.NewGuid()}", CT);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task DeleteServicePeriod_DoesNotExist_Returns404()
    {
        var response = await ManagerClient.DeleteAsync($"/api/service-periods/{Guid.NewGuid()}?operationId={Guid.NewGuid()}", CT);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DeleteServicePeriod_CallerIsFloorStaff_Returns403()
    {
        var created = await ManagerClient.PostAsJsonAsync("/api/service-periods", ValidBody(label: "Protected From Staff"), CT);
        var servicePeriodId = (await created.Content.ReadFromJsonAsync<JsonDocument>(CT))!.RootElement.GetProperty("id").GetGuid();
        var floorStaffClient = fixture.CreateAuthenticatedClient(Guid.NewGuid(), role: "FloorStaff");

        var response = await floorStaffClient.DeleteAsync($"/api/service-periods/{servicePeriodId}?operationId={Guid.NewGuid()}", CT);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task DeleteServicePeriod_SameOperationIdTwice_ReturnsNoContentBothTimesAndDeletesOnlyOnce()
    {
        var created = await ManagerClient.PostAsJsonAsync("/api/service-periods", ValidBody(label: "Replay Delete"), CT);
        var servicePeriodId = (await created.Content.ReadFromJsonAsync<JsonDocument>(CT))!.RootElement.GetProperty("id").GetGuid();
        var operationId = Guid.NewGuid();

        var first = await ManagerClient.DeleteAsync($"/api/service-periods/{servicePeriodId}?operationId={operationId}", CT);
        var second = await ManagerClient.DeleteAsync($"/api/service-periods/{servicePeriodId}?operationId={operationId}", CT);

        first.StatusCode.Should().Be(HttpStatusCode.NoContent);
        second.StatusCode.Should().Be(HttpStatusCode.NoContent,
            because: "OperationId idempotency: replaying the same delete must not 404 on the second call.");
    }
}
