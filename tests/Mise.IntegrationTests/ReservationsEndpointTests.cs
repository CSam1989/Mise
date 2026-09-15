using System.Net.Http.Json;
using System.Text.Json;

namespace Mise.IntegrationTests;

[Trait("Category", "Integration")]
[Collection(ReservationsApiCollection.Name)]
public class ReservationsEndpointTests(ReservationsApiFixture fixture)
{
    private static CancellationToken CT => TestContext.Current.CancellationToken;

    private static object ValidBody(Guid? operationId = null, int partySize = 4, string customerName = "Jane Doe") => new
    {
        OperationId = operationId ?? Guid.NewGuid(),
        CustomerName = customerName,
        PartySize = partySize,
        ReservationDateTime = DateTimeOffset.UtcNow.AddDays(1),
    };

    [Fact]
    public async Task PostReservation_ValidBody_Returns201WithLocation()
    {
        var client = fixture.CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync("/api/reservations", ValidBody(), CT);

        response.StatusCode.Should().Be(HttpStatusCode.Created, because: "a valid reservation request must be accepted.");
        response.Headers.Location.Should().NotBeNull(
            because: "a 201 must carry a Location header pointing at the created resource.");

        var json = await response.Content.ReadFromJsonAsync<JsonDocument>(CT);
        json!.RootElement.GetProperty("customerName").GetString().Should().Be("Jane Doe");
        json.RootElement.GetProperty("partySize").GetInt32().Should().Be(4);
        json.RootElement.GetProperty("status").GetString().Should().Be("Confirmed");
    }

    [Fact]
    public async Task PostReservation_PartySizeZero_Returns400WithFieldError()
    {
        var client = fixture.CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync("/api/reservations", ValidBody(partySize: 0), CT);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var json = await response.Content.ReadFromJsonAsync<JsonDocument>(CT);
        var errors = json!.RootElement.GetProperty("errors");
        errors.GetProperty("PartySize")[0].GetString().Should().Be(
            "Party size is required and must be more than 0.",
            because: "the field-scoped error must name PartySize with the exact validator message the UI shows.");
    }

    [Fact]
    public async Task PostReservation_Unauthenticated_Returns401()
    {
        var client = fixture.CreateClient();

        var response = await client.PostAsJsonAsync("/api/reservations", ValidBody(), CT);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            because: "the global fallback authorization policy requires authentication by default.");
    }

    [Fact]
    public async Task CreateReservation_SameOperationIdTwice_CreatesExactlyOneReservation()
    {
        var client = fixture.CreateAuthenticatedClient();
        var body = ValidBody(operationId: Guid.NewGuid(), customerName: "Replay Test");

        var first = await client.PostAsJsonAsync("/api/reservations", body, CT);
        var second = await client.PostAsJsonAsync("/api/reservations", body, CT);

        first.StatusCode.Should().Be(HttpStatusCode.Created);
        second.StatusCode.Should().Be(HttpStatusCode.Created,
            because: "a replayed OperationId must still succeed — idempotent, not rejected.");

        var firstJson = await first.Content.ReadFromJsonAsync<JsonDocument>(CT);
        var secondJson = await second.Content.ReadFromJsonAsync<JsonDocument>(CT);
        secondJson!.RootElement.GetProperty("id").GetGuid().Should().Be(
            firstJson!.RootElement.GetProperty("id").GetGuid(),
            because: "replaying the same OperationId must return the same reservation id, not create a second row (docs/plan.md's OperationId replay contract).");
    }

    [Fact]
    public async Task PostReservation_ValidBody_WritesExactlyOneMatchingAuditLogEntry()
    {
        var client = fixture.CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync("/api/reservations", ValidBody(customerName: "Audit Check"), CT);
        var json = await response.Content.ReadFromJsonAsync<JsonDocument>(CT);
        var reservationId = json!.RootElement.GetProperty("id").GetGuid();

        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(CT);
        await using var command = connection.CreateCommand();
        command.CommandText =
            "select performed_by_system_process from shared.audit_log_entry where entity_id = @id and action = 'Created'";
        command.Parameters.AddWithValue("id", reservationId);
        await using var reader = await command.ExecuteReaderAsync(CT);

        (await reader.ReadAsync(CT)).Should().BeTrue(because: "creating a reservation must write exactly one matching audit entry.");
        reader.GetString(0).Should().Be("integration-test",
            because: "PerformedBy must round-trip from the caller's JWT Name claim through to the audit entry — not just be present, but be the right value.");
        (await reader.ReadAsync(CT)).Should().BeFalse(because: "exactly one audit entry, not more.");
    }
}
