using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.SignalR.Client;

namespace Mise.IntegrationTests;

/// <summary>
/// Phase 8 (FR-10/NFR-04/US-03). Per docs/plan.md's "Testing the hard subsystems" guidance:
/// assert *arrival*, never *timing* — every wait below is a <see cref="TaskCompletionSource"/>
/// set from the hub's own event handler, bounded by a generous <see cref="Task.WaitAsync(TimeSpan)"/>
/// timeout (a signal-driven wait, not a forbidden Thread.Sleep/Task.Delay). The event names
/// ("TableStatusChanged", "ReservationCreated", "ReservationUpdated", "ReservationCancelled")
/// are the frozen wire contract — they must exactly match
/// <c>Mise.ApiService.Realtime.SignalRRealtimeNotifier</c>'s own constants; a mismatch here would
/// surface as every test below timing out, not a compile error, which is exactly why these tests
/// exist. The "exactly once on replay" guarantee itself is proven at the unit tier instead
/// (<c>Times.Never</c> on <c>IRealtimeNotifier</c> for every handler's replay path) — a client
/// delivery round-trip over long-polling has no reliable "nothing else is coming" signal that
/// doesn't amount to a disguised sleep, so this tier sticks to positive-arrival proofs only.
/// </summary>
[Trait("Category", "Integration")]
[Collection(MiseApiCollection.Name)]
public class FloorPlanHubTests(MiseApiFixture fixture)
{
    private static CancellationToken CT => TestContext.Current.CancellationToken;
    private static readonly TimeSpan ArrivalTimeout = TimeSpan.FromSeconds(10);

    private HttpClient ManagerClient => fixture.CreateAuthenticatedClient(role: "Manager");

    private async Task<Guid> CreateSectionAsync()
    {
        var response = await ManagerClient.PostAsJsonAsync(
            "/api/sections", new { OperationId = Guid.NewGuid(), Name = $"Section-{Guid.NewGuid():N}", DisplayOrder = 0 }, CT);
        var json = await response.Content.ReadFromJsonAsync<JsonDocument>(CT);
        return json!.RootElement.GetProperty("id").GetGuid();
    }

    private async Task<Guid> CreateTableAsync(int minCapacity = 2, int maxCapacity = 6) => (await CreateTableWithETagAsync(minCapacity, maxCapacity)).Id;

    private async Task<(Guid Id, string ETag)> CreateTableWithETagAsync(int minCapacity = 2, int maxCapacity = 6)
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
                IsCombinable = false,
                PositionX = (double?)null,
                PositionY = (double?)null,
            },
            CT);
        var json = await response.Content.ReadFromJsonAsync<JsonDocument>(CT);
        return (json!.RootElement.GetProperty("id").GetGuid(), response.Headers.ETag!.Tag);
    }

    private static object ValidReservationBody(Guid? tableId = null, int partySize = 4) => new
    {
        OperationId = Guid.NewGuid(),
        CustomerName = "Jane Doe",
        CustomerPhone = "+32 470 00 00 00",
        PartySize = partySize,
        ReservationDateTime = DateTimeOffset.UtcNow.AddDays(1),
        TableId = tableId,
    };

    private async Task<(Guid Id, string ETag)> CreateReservationAsync(Guid? tableId = null, int partySize = 4)
    {
        var response = await ManagerClient.PostAsJsonAsync("/api/reservations", ValidReservationBody(tableId, partySize), CT);
        var json = await response.Content.ReadFromJsonAsync<JsonDocument>(CT);
        return (json!.RootElement.GetProperty("id").GetGuid(), response.Headers.ETag!.Tag);
    }

    private sealed record ReservationChangedWireDto(
        Guid ReservationId, string CustomerName, int PartySize, DateTimeOffset ReservationDateTime, string Status, Guid? TableId);

    private sealed record TableStatusChangedWireDto(Guid TableId, string Status);

    [Fact]
    public async Task Connect_Unauthenticated_Rejected()
    {
        await using var connection = fixture.CreateHubConnection(role: null);

        var act = () => connection.StartAsync(CT);

        await act.Should().ThrowAsync<HttpRequestException>(
            because: "FloorPlanHub requires the FloorStaff policy, the same 401 guarantee every REST endpoint gives — SignalR's negotiate step surfaces a failed handshake as this exception type.");
    }

    [Fact]
    public async Task Connect_AuthenticatedFloorStaff_Succeeds()
    {
        await using var connection = fixture.CreateHubConnection(role: "FloorStaff");

        await connection.StartAsync(CT);

        connection.State.Should().Be(HubConnectionState.Connected);
    }

    [Fact]
    public async Task PostReservation_BroadcastsReservationCreated()
    {
        var arrived = new TaskCompletionSource<ReservationChangedWireDto>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var connection = fixture.CreateHubConnection();
        connection.On<ReservationChangedWireDto>("ReservationCreated", dto => arrived.TrySetResult(dto));
        await connection.StartAsync(CT);

        var response = await ManagerClient.PostAsJsonAsync("/api/reservations", ValidReservationBody(), CT);
        var reservationId = (await response.Content.ReadFromJsonAsync<JsonDocument>(CT))!.RootElement.GetProperty("id").GetGuid();

        var received = await arrived.Task.WaitAsync(ArrivalTimeout, CT);
        received.ReservationId.Should().Be(reservationId);
        received.Status.Should().Be("Confirmed");
    }

    [Fact]
    public async Task PatchReservation_BroadcastsReservationUpdated()
    {
        var (id, etag) = await CreateReservationAsync();
        var arrived = new TaskCompletionSource<ReservationChangedWireDto>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var connection = fixture.CreateHubConnection();
        connection.On<ReservationChangedWireDto>("ReservationUpdated", dto => arrived.TrySetResult(dto));
        await connection.StartAsync(CT);

        var request = new HttpRequestMessage(HttpMethod.Patch, $"/api/reservations/{id}")
        {
            Content = JsonContent.Create(new
            {
                OperationId = Guid.NewGuid(),
                CustomerName = "John Smith",
                CustomerPhone = "+32 470 11 11 11",
                PartySize = 6,
                ReservationDateTime = DateTimeOffset.UtcNow.AddDays(1),
                DurationMinutes = 90,
                TableId = (Guid?)null,
                Notes = (string?)null,
            }),
        };
        request.Headers.TryAddWithoutValidation("If-Match", etag);
        await ManagerClient.SendAsync(request, CT);

        var received = await arrived.Task.WaitAsync(ArrivalTimeout, CT);
        received.ReservationId.Should().Be(id);
        received.CustomerName.Should().Be("John Smith");
    }

    [Fact]
    public async Task PatchReservationSeat_BroadcastsReservationUpdatedAndTableStatusChanged()
    {
        var (id, etag) = await CreateReservationAsync();
        var tableId = await CreateTableAsync();
        var reservationArrived = new TaskCompletionSource<ReservationChangedWireDto>(TaskCreationOptions.RunContinuationsAsynchronously);
        var tableArrived = new TaskCompletionSource<TableStatusChangedWireDto>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var connection = fixture.CreateHubConnection();
        connection.On<ReservationChangedWireDto>("ReservationUpdated", dto => reservationArrived.TrySetResult(dto));
        connection.On<TableStatusChangedWireDto>("TableStatusChanged", dto => tableArrived.TrySetResult(dto));
        await connection.StartAsync(CT);

        var request = new HttpRequestMessage(HttpMethod.Patch, $"/api/reservations/{id}/seat")
        {
            Content = JsonContent.Create(new { OperationId = Guid.NewGuid(), TableId = tableId }),
        };
        request.Headers.TryAddWithoutValidation("If-Match", etag);
        await ManagerClient.SendAsync(request, CT);

        var reservationEvent = await reservationArrived.Task.WaitAsync(ArrivalTimeout, CT);
        reservationEvent.Status.Should().Be("Seated", because: "ADR-008 collapses Seat into ReservationUpdated, not a distinct wire event.");
        var tableEvent = await tableArrived.Task.WaitAsync(ArrivalTimeout, CT);
        tableEvent.TableId.Should().Be(tableId);
        tableEvent.Status.Should().Be("Occupied");
    }

    [Fact]
    public async Task PatchReservationCancel_AfterSeat_BroadcastsReservationCancelledAndTableStatusChangedBackToAvailable()
    {
        var (id, etag) = await CreateReservationAsync();
        var tableId = await CreateTableAsync();
        var seatRequest = new HttpRequestMessage(HttpMethod.Patch, $"/api/reservations/{id}/seat")
        {
            Content = JsonContent.Create(new { OperationId = Guid.NewGuid(), TableId = tableId }),
        };
        seatRequest.Headers.TryAddWithoutValidation("If-Match", etag);
        var seatResponse = await ManagerClient.SendAsync(seatRequest, CT);
        var seatedEtag = seatResponse.Headers.ETag!.Tag;

        var cancelledArrived = new TaskCompletionSource<ReservationChangedWireDto>(TaskCreationOptions.RunContinuationsAsynchronously);
        var tableArrived = new TaskCompletionSource<TableStatusChangedWireDto>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var connection = fixture.CreateHubConnection();
        connection.On<ReservationChangedWireDto>("ReservationCancelled", dto => cancelledArrived.TrySetResult(dto));
        connection.On<TableStatusChangedWireDto>("TableStatusChanged", dto => tableArrived.TrySetResult(dto));
        await connection.StartAsync(CT);

        var cancelRequest = new HttpRequestMessage(HttpMethod.Patch, $"/api/reservations/{id}/cancel")
        {
            Content = JsonContent.Create(new { OperationId = Guid.NewGuid() }),
        };
        cancelRequest.Headers.TryAddWithoutValidation("If-Match", seatedEtag);
        await ManagerClient.SendAsync(cancelRequest, CT);

        var cancelledEvent = await cancelledArrived.Task.WaitAsync(ArrivalTimeout, CT);
        cancelledEvent.ReservationId.Should().Be(id);
        cancelledEvent.Status.Should().Be("Cancelled");
        var tableEvent = await tableArrived.Task.WaitAsync(ArrivalTimeout, CT);
        tableEvent.TableId.Should().Be(tableId);
        tableEvent.Status.Should().Be("Available", because: "BR-05 — cancelling a seated reservation frees its table.");
    }

    [Fact]
    public async Task PatchTableStatus_BroadcastsTableStatusChanged()
    {
        var (tableId, etag) = await CreateTableWithETagAsync();
        var floorStaffClient = fixture.CreateAuthenticatedClient(role: "FloorStaff");

        var arrived = new TaskCompletionSource<TableStatusChangedWireDto>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var connection = fixture.CreateHubConnection(role: "FloorStaff");
        connection.On<TableStatusChangedWireDto>("TableStatusChanged", dto => arrived.TrySetResult(dto));
        await connection.StartAsync(CT);

        var request = new HttpRequestMessage(HttpMethod.Patch, $"/api/tables/{tableId}/status")
        {
            Content = JsonContent.Create(new { OperationId = Guid.NewGuid(), Status = "NeedsCleaning" }),
        };
        request.Headers.TryAddWithoutValidation("If-Match", etag);
        await floorStaffClient.SendAsync(request, CT);

        var received = await arrived.Task.WaitAsync(ArrivalTimeout, CT);
        received.TableId.Should().Be(tableId);
        received.Status.Should().Be("NeedsCleaning");
    }
}
