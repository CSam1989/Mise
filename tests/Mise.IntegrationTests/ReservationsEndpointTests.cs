using System.Net.Http.Json;
using System.Text.Json;

namespace Mise.IntegrationTests;

[Trait("Category", "Integration")]
[Collection(MiseApiCollection.Name)]
public class ReservationsEndpointTests(MiseApiFixture fixture)
{
    private static CancellationToken CT => TestContext.Current.CancellationToken;

    private HttpClient ManagerClient => fixture.CreateAuthenticatedClient(role: "Manager");

    private static object ValidBody(
        Guid? operationId = null, int partySize = 4, string customerName = "Jane Doe",
        string customerPhone = "+32 470 00 00 00", DateTimeOffset? reservationDateTime = null,
        Guid? tableId = null, int? durationMinutes = null, string? customerEmail = null, string? notes = null) => new
        {
            OperationId = operationId ?? Guid.NewGuid(),
            CustomerName = customerName,
            CustomerPhone = customerPhone,
            PartySize = partySize,
            ReservationDateTime = reservationDateTime ?? DateTimeOffset.UtcNow.AddDays(1),
            DurationMinutes = durationMinutes,
            TableId = tableId,
            CustomerEmail = customerEmail,
            Notes = notes,
        };

    private async Task<Guid> CreateSectionAsync()
    {
        var response = await ManagerClient.PostAsJsonAsync(
            "/api/sections", new { OperationId = Guid.NewGuid(), Name = $"Section-{Guid.NewGuid():N}", DisplayOrder = 0 }, CT);
        var json = await response.Content.ReadFromJsonAsync<JsonDocument>(CT);
        return json!.RootElement.GetProperty("id").GetGuid();
    }

    private async Task<Guid> CreateTableAsync(int minCapacity = 2, int maxCapacity = 4, bool isCombinable = false)
    {
        var sectionId = await CreateSectionAsync();
        var response = await ManagerClient.PostAsJsonAsync(
            "/api/tables",
            new
            {
                OperationId = Guid.NewGuid(),
                SectionId = sectionId,
                Name = $"T-{Guid.NewGuid():N}",
                MinCapacity = minCapacity,
                MaxCapacity = maxCapacity,
                IsCombinable = isCombinable,
                PositionX = (double?)null,
                PositionY = (double?)null,
            },
            CT);
        var json = await response.Content.ReadFromJsonAsync<JsonDocument>(CT);
        return json!.RootElement.GetProperty("id").GetGuid();
    }

    private async Task<Guid> CreateTableGroupAsync(params Guid[] tableIds)
    {
        var response = await ManagerClient.PostAsJsonAsync(
            "/api/table-groups", new { OperationId = Guid.NewGuid(), Name = $"Group-{Guid.NewGuid():N}", TableIds = tableIds }, CT);
        var json = await response.Content.ReadFromJsonAsync<JsonDocument>(CT);
        return json!.RootElement.GetProperty("id").GetGuid();
    }

    // --- Create --------------------------------------------------------------------------

    [Fact]
    public async Task PostReservation_ValidBody_Returns201WithLocationAndETag()
    {
        var client = fixture.CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync("/api/reservations", ValidBody(), CT);

        response.StatusCode.Should().Be(HttpStatusCode.Created, because: "a valid reservation request must be accepted.");
        response.Headers.Location.Should().NotBeNull();
        response.Headers.ETag.Should().NotBeNull(because: "docs/plan.md correction #5, extended to Reservation in Phase 6.");

        var json = await response.Content.ReadFromJsonAsync<JsonDocument>(CT);
        json!.RootElement.GetProperty("customerName").GetString().Should().Be("Jane Doe");
        json.RootElement.GetProperty("customerPhone").GetString().Should().Be("+32 470 00 00 00");
        json.RootElement.GetProperty("partySize").GetInt32().Should().Be(4);
        json.RootElement.GetProperty("status").GetString().Should().Be("Confirmed");
        json.RootElement.GetProperty("durationMinutes").GetInt32().Should().Be(90,
            because: "an omitted DurationMinutes falls back to the configured default (Decision #7).");
    }

    [Fact]
    public async Task PostReservation_PartySizeZero_Returns400WithFieldError()
    {
        var client = fixture.CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync("/api/reservations", ValidBody(partySize: 0), CT);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var json = await response.Content.ReadFromJsonAsync<JsonDocument>(CT);
        json!.RootElement.GetProperty("errors").GetProperty("PartySize")[0].GetString().Should().Be(
            "Party size is required and must be more than 0.",
            because: "the field-scoped error must name PartySize with the exact validator message the UI shows.");
    }

    [Fact]
    public async Task PostReservation_CustomerPhoneEmpty_Returns400WithFieldError()
    {
        var client = fixture.CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync("/api/reservations", ValidBody(customerPhone: ""), CT);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var json = await response.Content.ReadFromJsonAsync<JsonDocument>(CT);
        json!.RootElement.GetProperty("errors").GetProperty("CustomerPhone")[0].GetString().Should().Be("CustomerPhone is required.");
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
    public async Task PostReservation_CallerIsFloorStaff_Returns201()
    {
        var floorStaffClient = fixture.CreateAuthenticatedClient(Guid.NewGuid(), role: "FloorStaff");

        var response = await floorStaffClient.PostAsJsonAsync("/api/reservations", ValidBody(), CT);

        response.StatusCode.Should().Be(HttpStatusCode.Created,
            because: "FR-01–03 name no permission boundary between FloorStaff and Manager — unlike Tables/Scheduling's Manager-only mutations.");
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
        secondJson!.RootElement.GetProperty("id").GetGuid().Should().Be(firstJson!.RootElement.GetProperty("id").GetGuid());
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
            "select performed_by_staff_id from shared.audit_log_entry where entity_id = @id and action = 'Created'";
        command.Parameters.AddWithValue("id", reservationId);
        await using var reader = await command.ExecuteReaderAsync(CT);

        (await reader.ReadAsync(CT)).Should().BeTrue(because: "creating a reservation must write exactly one matching audit entry.");
        reader.GetGuid(0).Should().Be(MiseApiFixture.DefaultStaffId);
        (await reader.ReadAsync(CT)).Should().BeFalse(because: "exactly one audit entry, not more.");
    }

    [Fact]
    public async Task PostReservation_TableIdDoesNotExist_Returns400WithFieldError()
    {
        var client = fixture.CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync("/api/reservations", ValidBody(tableId: Guid.NewGuid()), CT);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var json = await response.Content.ReadFromJsonAsync<JsonDocument>(CT);
        json!.RootElement.GetProperty("errors").GetProperty("TableId")[0].GetString()
            .Should().Be("TableId does not refer to an existing table.");
    }

    [Fact]
    public async Task PostReservation_PartySizeExceedsTableCapacity_Returns400WithFieldError()
    {
        var client = fixture.CreateAuthenticatedClient();
        var tableId = await CreateTableAsync(minCapacity: 2, maxCapacity: 4);

        var response = await client.PostAsJsonAsync("/api/reservations", ValidBody(partySize: 8, tableId: tableId), CT);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, because: "BR-07: party size must fit the assigned table's capacity.");
        var json = await response.Content.ReadFromJsonAsync<JsonDocument>(CT);
        json!.RootElement.GetProperty("errors").GetProperty("PartySize")[0].GetString()
            .Should().Be("PartySize does not fit the assigned table's capacity.");
    }

    [Fact]
    public async Task PostReservation_PartySizeWithinCombinedGroupCapacity_Returns201()
    {
        var client = fixture.CreateAuthenticatedClient();
        var tableA = await CreateTableAsync(minCapacity: 2, maxCapacity: 4, isCombinable: true);
        var tableB = await CreateTableAsync(minCapacity: 2, maxCapacity: 4, isCombinable: true);
        await CreateTableGroupAsync(tableA, tableB);

        var response = await client.PostAsJsonAsync("/api/reservations", ValidBody(partySize: 8, tableId: tableA), CT);

        response.StatusCode.Should().Be(HttpStatusCode.Created,
            because: "BR-07's 'or an explicitly combinable set of tables' — the group's combined capacity (4+4=8) fits.");
    }

    [Fact]
    public async Task PostReservation_TableAlreadyBooked_Returns409()
    {
        var client = fixture.CreateAuthenticatedClient();
        var tableId = await CreateTableAsync();
        var reservationDateTime = DateTimeOffset.UtcNow.AddDays(5);
        var first = await client.PostAsJsonAsync(
            "/api/reservations", ValidBody(tableId: tableId, reservationDateTime: reservationDateTime), CT);
        first.StatusCode.Should().Be(HttpStatusCode.Created);

        var overlapping = reservationDateTime.AddMinutes(30);
        var response = await client.PostAsJsonAsync(
            "/api/reservations", ValidBody(tableId: tableId, reservationDateTime: overlapping, customerName: "Second Booking"), CT);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict,
            because: "docs/plan.md correction #1 (BR-01) — an overlapping reservation on the same table is a clean 409.");
    }

    [Fact]
    public async Task CreateReservation_TwoConcurrentOverlappingInserts_ExactlyOneSucceeds()
    {
        // The hard-subsystem proof (docs/plan.md correction #1): the application-level
        // pre-check (proven by PostReservation_TableAlreadyBooked_Returns409 above) can't close
        // a TOCTOU race between two genuinely concurrent transactions — only the Postgres
        // exclusion constraint itself can. Bypasses the API/Application layers entirely and
        // drives two raw, real transactions over two independent connections, launched together
        // via Task.WhenAll — two real network round-trips to a real Postgres reliably overlap
        // without needing a manual barrier (an earlier version used a System.Threading.Barrier
        // here; it added a real hang risk — if either connection's open is ever slow, the other
        // waits on the barrier indefinitely — for no actual gain, since Postgres's own locking is
        // what makes this test valid regardless of the exact submission timing).
        var tableId = Guid.NewGuid();
        var reservationDateTime = DateTimeOffset.UtcNow.AddDays(60);

        async Task<Exception?> InsertAndCommitAsync()
        {
            await using var connection = new NpgsqlConnection(fixture.ConnectionString);
            await connection.OpenAsync(CT);
            await using var transaction = await connection.BeginTransactionAsync(CT);
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText =
                """
                insert into reservations.reservation
                    (id, customer_name, customer_phone, party_size, reservation_date_time, duration_minutes, status,
                     table_id, created_by_staff_id, created_at_utc, updated_at_utc)
                values (@id, 'Concurrency Test', '+32000000000', 2, @dt, 90, 'Confirmed', @tableId, @staffId, now(), now())
                """;
            command.Parameters.AddWithValue("id", Guid.NewGuid());
            command.Parameters.AddWithValue("dt", reservationDateTime);
            command.Parameters.AddWithValue("tableId", tableId);
            command.Parameters.AddWithValue("staffId", MiseApiFixture.DefaultStaffId);

            try
            {
                await command.ExecuteNonQueryAsync(CT);
                await transaction.CommitAsync(CT);
                return null;
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync(CT);
                return ex;
            }
        }

        var results = await Task.WhenAll(InsertAndCommitAsync(), InsertAndCommitAsync());

        results.Count(r => r is null).Should().Be(1, because: "exactly one of the two concurrent inserts must win.");
        var loser = results.SingleOrDefault(r => r is not null);
        loser.Should().BeOfType<PostgresException>(
            because: "the loser must be rejected by the exclusion constraint itself, not an application-level check.");
        ((PostgresException)loser!).SqlState.Should().Be(PostgresErrorCodes.ExclusionViolation);
    }

    // --- Update ----------------------------------------------------------------------------

    private async Task<(Guid Id, string ETag)> CreateReservationAsync(DateTimeOffset? reservationDateTime = null)
    {
        var response = await fixture.CreateAuthenticatedClient()
            .PostAsJsonAsync("/api/reservations", ValidBody(reservationDateTime: reservationDateTime), CT);
        var json = await response.Content.ReadFromJsonAsync<JsonDocument>(CT);
        return (json!.RootElement.GetProperty("id").GetGuid(), response.Headers.ETag!.Tag);
    }

    private static object UpdateBody(
        string customerName = "John Smith", string customerPhone = "+32 470 11 11 11", int partySize = 6,
        DateTimeOffset? reservationDateTime = null, int durationMinutes = 120, Guid? tableId = null) => new
        {
            OperationId = Guid.NewGuid(),
            CustomerName = customerName,
            CustomerPhone = customerPhone,
            PartySize = partySize,
            ReservationDateTime = reservationDateTime ?? DateTimeOffset.UtcNow.AddDays(1),
            DurationMinutes = durationMinutes,
            TableId = tableId,
            CustomerEmail = (string?)null,
            Notes = (string?)null,
        };

    [Fact]
    public async Task PatchReservation_MissingIfMatch_Returns428()
    {
        var (id, _) = await CreateReservationAsync();

        var response = await ManagerClient.PatchAsJsonAsync($"/api/reservations/{id}", UpdateBody(), CT);

        response.StatusCode.Should().Be((HttpStatusCode)428);
    }

    [Fact]
    public async Task PatchReservation_ValidIfMatch_Returns200AndUpdatesFieldsWithNewETag()
    {
        var (id, etag) = await CreateReservationAsync();

        var request = new HttpRequestMessage(HttpMethod.Patch, $"/api/reservations/{id}")
        {
            Content = JsonContent.Create(UpdateBody(customerName: "Updated Name")),
        };
        request.Headers.TryAddWithoutValidation("If-Match", etag);
        var response = await ManagerClient.SendAsync(request, CT);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.ETag!.Tag.Should().NotBe(etag, because: "a successful update must advance the version.");
        var json = await response.Content.ReadFromJsonAsync<JsonDocument>(CT);
        json!.RootElement.GetProperty("customerName").GetString().Should().Be("Updated Name");
        json.RootElement.GetProperty("partySize").GetInt32().Should().Be(6);
    }

    [Fact]
    public async Task PatchReservation_ReservationDoesNotExist_Returns404()
    {
        var request = new HttpRequestMessage(HttpMethod.Patch, $"/api/reservations/{Guid.NewGuid()}")
        {
            Content = JsonContent.Create(UpdateBody()),
        };
        request.Headers.TryAddWithoutValidation("If-Match", "\"1\"");
        var response = await ManagerClient.SendAsync(request, CT);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task PatchReservation_StaleIfMatch_Returns409WithCurrentState()
    {
        var (id, originalEtag) = await CreateReservationAsync();

        var firstUpdate = new HttpRequestMessage(HttpMethod.Patch, $"/api/reservations/{id}")
        {
            Content = JsonContent.Create(UpdateBody(customerName: "First Update")),
        };
        firstUpdate.Headers.TryAddWithoutValidation("If-Match", originalEtag);
        (await ManagerClient.SendAsync(firstUpdate, CT)).StatusCode.Should().Be(HttpStatusCode.OK);

        var staleUpdate = new HttpRequestMessage(HttpMethod.Patch, $"/api/reservations/{id}")
        {
            Content = JsonContent.Create(UpdateBody(customerName: "Second Update Using Stale ETag")),
        };
        staleUpdate.Headers.TryAddWithoutValidation("If-Match", originalEtag);
        var response = await ManagerClient.SendAsync(staleUpdate, CT);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict,
            because: "docs/plan.md correction #5 — a stale If-Match must be rejected with 409, never a silent overwrite (NFR-03).");
        var json = await response.Content.ReadFromJsonAsync<JsonDocument>(CT);
        json!.RootElement.GetProperty("currentState").GetProperty("customerName").GetString().Should().Be("First Update");
    }

    [Fact]
    public async Task PatchReservation_PartySizeExceedsTableCapacity_Returns400()
    {
        var (id, etag) = await CreateReservationAsync();
        var tableId = await CreateTableAsync(minCapacity: 2, maxCapacity: 4);

        var request = new HttpRequestMessage(HttpMethod.Patch, $"/api/reservations/{id}")
        {
            Content = JsonContent.Create(UpdateBody(partySize: 8, tableId: tableId)),
        };
        request.Headers.TryAddWithoutValidation("If-Match", etag);
        var response = await ManagerClient.SendAsync(request, CT);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task PatchReservation_ResultingOverlapWithAnotherReservation_Returns409()
    {
        var tableId = await CreateTableAsync();
        var occupiedTime = DateTimeOffset.UtcNow.AddDays(10);
        await ManagerClient.PostAsJsonAsync(
            "/api/reservations", ValidBody(tableId: tableId, reservationDateTime: occupiedTime, customerName: "Blocking Reservation"), CT);

        var (id, etag) = await CreateReservationAsync(reservationDateTime: DateTimeOffset.UtcNow.AddDays(20));
        var request = new HttpRequestMessage(HttpMethod.Patch, $"/api/reservations/{id}")
        {
            Content = JsonContent.Create(UpdateBody(partySize: 2, reservationDateTime: occupiedTime.AddMinutes(15), tableId: tableId)),
        };
        request.Headers.TryAddWithoutValidation("If-Match", etag);
        var response = await ManagerClient.SendAsync(request, CT);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict, because: "BR-01 applies to Update exactly as it does to Create.");
    }

    [Fact]
    public async Task PatchReservation_ReservationAlreadyCancelled_Returns409()
    {
        var (id, etag) = await CreateReservationAsync();
        var cancelRequest = new HttpRequestMessage(HttpMethod.Patch, $"/api/reservations/{id}/cancel")
        {
            Content = JsonContent.Create(new { OperationId = Guid.NewGuid() }),
        };
        cancelRequest.Headers.TryAddWithoutValidation("If-Match", etag);
        var cancelled = await ManagerClient.SendAsync(cancelRequest, CT);
        var cancelledEtag = cancelled.Headers.ETag!.Tag;

        var updateRequest = new HttpRequestMessage(HttpMethod.Patch, $"/api/reservations/{id}")
        {
            Content = JsonContent.Create(UpdateBody()),
        };
        updateRequest.Headers.TryAddWithoutValidation("If-Match", cancelledEtag);
        var response = await ManagerClient.SendAsync(updateRequest, CT);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict,
            because: "editing an already-cancelled reservation is a self-contained Domain rule violation (409), same category as Table.Deactivate()'s guard.");
    }

    [Fact]
    public async Task PatchReservation_ValidBody_WritesExactlyOneMatchingAuditLogEntry()
    {
        var (id, etag) = await CreateReservationAsync();
        var request = new HttpRequestMessage(HttpMethod.Patch, $"/api/reservations/{id}")
        {
            Content = JsonContent.Create(UpdateBody()),
        };
        request.Headers.TryAddWithoutValidation("If-Match", etag);
        await ManagerClient.SendAsync(request, CT);

        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(CT);
        await using var command = connection.CreateCommand();
        command.CommandText = "select count(*) from shared.audit_log_entry where entity_id = @id and action = 'Updated'";
        command.Parameters.AddWithValue("id", id);
        (await command.ExecuteScalarAsync(CT)).Should().Be(1L);
    }

    [Fact]
    public async Task PatchReservation_SameOperationIdTwice_UpdatesOnlyOnce()
    {
        var (id, etag) = await CreateReservationAsync();
        var operationId = Guid.NewGuid();
        var body = new
        {
            OperationId = operationId,
            CustomerName = "Replay Update",
            CustomerPhone = "+32 470 11 11 11",
            PartySize = 6,
            ReservationDateTime = DateTimeOffset.UtcNow.AddDays(1),
            DurationMinutes = 120,
            TableId = (Guid?)null,
            CustomerEmail = (string?)null,
            Notes = (string?)null,
        };

        var request1 = new HttpRequestMessage(HttpMethod.Patch, $"/api/reservations/{id}") { Content = JsonContent.Create(body) };
        request1.Headers.TryAddWithoutValidation("If-Match", etag);
        var first = await ManagerClient.SendAsync(request1, CT);

        var request2 = new HttpRequestMessage(HttpMethod.Patch, $"/api/reservations/{id}") { Content = JsonContent.Create(body) };
        request2.Headers.TryAddWithoutValidation("If-Match", etag);
        var second = await ManagerClient.SendAsync(request2, CT);

        first.StatusCode.Should().Be(HttpStatusCode.OK);
        second.StatusCode.Should().Be(HttpStatusCode.OK, because: "a replayed OperationId must still succeed — idempotent, not rejected.");

        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(CT);
        await using var command = connection.CreateCommand();
        command.CommandText = "select count(*) from shared.audit_log_entry where entity_id = @id and action = 'Updated'";
        command.Parameters.AddWithValue("id", id);
        (await command.ExecuteScalarAsync(CT)).Should().Be(1L, because: "the replay must not write a second audit entry for the same effect.");
    }

    // --- Cancel ----------------------------------------------------------------------------

    [Fact]
    public async Task PatchReservationCancel_MissingIfMatch_Returns428()
    {
        var (id, _) = await CreateReservationAsync();

        var response = await ManagerClient.PatchAsJsonAsync($"/api/reservations/{id}/cancel", new { OperationId = Guid.NewGuid() }, CT);

        response.StatusCode.Should().Be((HttpStatusCode)428);
    }

    [Fact]
    public async Task PatchReservationCancel_ReservationDoesNotExist_Returns404()
    {
        var request = new HttpRequestMessage(HttpMethod.Patch, $"/api/reservations/{Guid.NewGuid()}/cancel")
        {
            Content = JsonContent.Create(new { OperationId = Guid.NewGuid() }),
        };
        request.Headers.TryAddWithoutValidation("If-Match", "\"1\"");
        var response = await ManagerClient.SendAsync(request, CT);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task PatchReservationCancel_ValidIfMatch_Returns200AndStatusCancelled()
    {
        var (id, etag) = await CreateReservationAsync();

        var request = new HttpRequestMessage(HttpMethod.Patch, $"/api/reservations/{id}/cancel")
        {
            Content = JsonContent.Create(new { OperationId = Guid.NewGuid() }),
        };
        request.Headers.TryAddWithoutValidation("If-Match", etag);
        var response = await ManagerClient.SendAsync(request, CT);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonDocument>(CT);
        json!.RootElement.GetProperty("status").GetString().Should().Be("Cancelled");
    }

    [Fact]
    public async Task PatchReservationCancel_ValidIfMatch_WritesExactlyOneMatchingAuditLogEntry()
    {
        var (id, etag) = await CreateReservationAsync();
        var request = new HttpRequestMessage(HttpMethod.Patch, $"/api/reservations/{id}/cancel")
        {
            Content = JsonContent.Create(new { OperationId = Guid.NewGuid() }),
        };
        request.Headers.TryAddWithoutValidation("If-Match", etag);
        await ManagerClient.SendAsync(request, CT);

        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(CT);
        await using var command = connection.CreateCommand();
        command.CommandText = "select count(*) from shared.audit_log_entry where entity_id = @id and action = 'Cancelled'";
        command.Parameters.AddWithValue("id", id);
        (await command.ExecuteScalarAsync(CT)).Should().Be(1L);
    }

    [Fact]
    public async Task PatchReservationCancel_SameOperationIdTwice_CancelsOnlyOnce()
    {
        var (id, etag) = await CreateReservationAsync();
        var operationId = Guid.NewGuid();

        var request1 = new HttpRequestMessage(HttpMethod.Patch, $"/api/reservations/{id}/cancel")
        {
            Content = JsonContent.Create(new { OperationId = operationId }),
        };
        request1.Headers.TryAddWithoutValidation("If-Match", etag);
        var first = await ManagerClient.SendAsync(request1, CT);

        var request2 = new HttpRequestMessage(HttpMethod.Patch, $"/api/reservations/{id}/cancel")
        {
            Content = JsonContent.Create(new { OperationId = operationId }),
        };
        request2.Headers.TryAddWithoutValidation("If-Match", etag);
        var second = await ManagerClient.SendAsync(request2, CT);

        first.StatusCode.Should().Be(HttpStatusCode.OK);
        second.StatusCode.Should().Be(HttpStatusCode.OK, because: "a replayed OperationId must still succeed — idempotent, not rejected.");

        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(CT);
        await using var command = connection.CreateCommand();
        command.CommandText = "select count(*) from shared.audit_log_entry where entity_id = @id and action = 'Cancelled'";
        command.Parameters.AddWithValue("id", id);
        (await command.ExecuteScalarAsync(CT)).Should().Be(1L, because: "the replay must not write a second audit entry for the same effect.");
    }

    // --- Search (US-02/FR-02) ---------------------------------------------------------------

    [Fact]
    public async Task GetSearch_ByPartialName_ReturnsMatchingReservations()
    {
        var uniqueName = $"Searchable-{Guid.NewGuid():N}";
        await fixture.CreateAuthenticatedClient()
            .PostAsJsonAsync("/api/reservations", ValidBody(customerName: uniqueName), CT);

        var response = await fixture.CreateAuthenticatedClient(role: "FloorStaff")
            .GetAsync($"/api/reservations/search?query={uniqueName[..15]}", CT);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonDocument>(CT);
        json!.RootElement.EnumerateArray().Should().ContainSingle(e => e.GetProperty("customerName").GetString() == uniqueName);
    }

    [Fact]
    public async Task GetSearch_ByPartialPhone_ReturnsMatchingReservations()
    {
        var uniquePhone = $"+329{Random.Shared.Next(1000000, 9999999)}";
        await fixture.CreateAuthenticatedClient()
            .PostAsJsonAsync("/api/reservations", ValidBody(customerPhone: uniquePhone, customerName: "Phone Search"), CT);

        var response = await fixture.CreateAuthenticatedClient()
            .GetAsync($"/api/reservations/search?query={Uri.EscapeDataString(uniquePhone)}", CT);

        var json = await response.Content.ReadFromJsonAsync<JsonDocument>(CT);
        json!.RootElement.EnumerateArray().Should().ContainSingle(e => e.GetProperty("customerPhone").GetString() == uniquePhone);
    }

    [Fact]
    public async Task GetSearch_ByDate_ReturnsOnlyThatDatesReservations()
    {
        // DateTimeOffset.Date returns a plain DateTime (Kind: Unspecified) — implicitly
        // converting that back to DateTimeOffset later (e.g. via AddHours) would use the local
        // machine's timezone offset, not UTC, silently landing the reservation on the wrong side
        // of the search endpoint's UTC day boundary. Anchor explicitly instead.
        var targetDate = new DateTimeOffset(DateTime.UtcNow.Date.AddDays(100), TimeSpan.Zero);
        var uniqueName = $"Date-Search-{Guid.NewGuid():N}";
        await fixture.CreateAuthenticatedClient()
            .PostAsJsonAsync("/api/reservations", ValidBody(customerName: uniqueName, reservationDateTime: targetDate.AddHours(19)), CT);
        await fixture.CreateAuthenticatedClient()
            .PostAsJsonAsync(
                "/api/reservations", ValidBody(customerName: "Different Day", reservationDateTime: targetDate.AddDays(1).AddHours(19)), CT);

        var response = await fixture.CreateAuthenticatedClient()
            .GetAsync($"/api/reservations/search?date={targetDate:yyyy-MM-dd}", CT);

        var json = await response.Content.ReadFromJsonAsync<JsonDocument>(CT);
        var names = json!.RootElement.EnumerateArray().Select(e => e.GetProperty("customerName").GetString()).ToArray();
        names.Should().Contain(uniqueName);
        names.Should().NotContain("Different Day");
    }

    [Fact]
    public async Task GetSearch_NoMatches_ReturnsEmptyArray()
    {
        var response = await fixture.CreateAuthenticatedClient()
            .GetAsync($"/api/reservations/search?query=no-such-reservation-{Guid.NewGuid():N}", CT);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonDocument>(CT);
        json!.RootElement.GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task GetSearch_Unauthenticated_Returns401()
    {
        var response = await fixture.CreateClient().GetAsync("/api/reservations/search?query=x", CT);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
