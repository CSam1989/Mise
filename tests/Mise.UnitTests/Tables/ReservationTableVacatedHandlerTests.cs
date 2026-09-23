using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Mise.Modules.Reservations.Contracts;
using Mise.Modules.Tables.Application;
using Mise.Modules.Tables.Application.Ports;
using Mise.Modules.Tables.Domain;
using Mise.SharedKernel;
using Mise.SharedKernel.Infrastructure;
using Moq;

namespace Mise.UnitTests.Tables;

public class ReservationTableVacatedHandlerTests
{
    private readonly Mock<ITablesData> _tablesData = new();
    private readonly Mock<IReservationLookup> _reservationLookup = new();
    private readonly Mock<IAuditWriter> _auditWriter = new();
    private readonly Mock<IRealtimeNotifier> _realtimeNotifier = new();
    private readonly FakeTimeProvider _timeProvider = new(DateTimeOffset.Parse("2026-09-15T18:00:00+02:00"));
    private readonly ReservationTableVacatedHandler _sut;

    public ReservationTableVacatedHandlerTests()
    {
        _sut = new ReservationTableVacatedHandler(
            _tablesData.Object, _reservationLookup.Object, _auditWriter.Object, _realtimeNotifier.Object, _timeProvider,
            NullLogger<ReservationTableVacatedHandler>.Instance);
    }

    private static Table ExistingTable(TableStatus status = TableStatus.Occupied)
    {
        var table = Table.Create(Guid.NewGuid(), Guid.NewGuid(), "T12", 2, 4, false, null, null);
        table.SetStatus(status);
        return table;
    }

    private static ReservationTableVacated EventFor(Guid tableId) =>
        new(Guid.NewGuid(), tableId, Guid.NewGuid(), DateTimeOffset.Parse("2026-09-15T18:00:00+02:00"));

    private void AllowRelease(Guid tableId, Guid excludingReservationId, bool stillHeld) =>
        _reservationLookup
            .Setup(l => l.HasCurrentActiveReservationForTableAsync(tableId, excludingReservationId, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(stillHeld);

    [Fact]
    public async Task HandleAsync_TableOccupiedAndNotHeldByAnotherReservation_ReleasesAndWritesOneAuditEntry()
    {
        var table = ExistingTable(TableStatus.Occupied);
        var domainEvent = EventFor(table.Id);
        _tablesData.Setup(d => d.GetTableByIdAsync(table.Id, It.IsAny<CancellationToken>())).ReturnsAsync(new TableWithVersion(table, 1));
        AllowRelease(table.Id, domainEvent.ReservationId, stillHeld: false);
        _tablesData
            .Setup(d => d.ChangeTableStatusAsync(It.IsAny<Table>(), 1, It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TableSaveResult(TableSaveOutcome.Saved, table, 2, WasAlreadyProcessed: false));

        await _sut.HandleAsync(domainEvent, CancellationToken.None);

        table.Status.Should().Be(TableStatus.Available);
        _auditWriter.Verify(
            a => a.WriteAsync(It.Is<AuditLogEntry>(e => e.EntityType == "Table" && e.EntityId == table.Id && e.Action == "Released"), It.IsAny<CancellationToken>()),
            Times.Once);
        _realtimeNotifier.Verify(
            n => n.NotifyTableStatusChangedAsync(
                It.Is<TableStatusChangedNotification>(p => p.TableId == table.Id && p.Status == "Available"), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task HandleAsync_AnotherActiveReservationCurrentlyHoldsTheTable_LeavesTableUntouched()
    {
        var table = ExistingTable(TableStatus.Occupied);
        var domainEvent = EventFor(table.Id);
        _tablesData.Setup(d => d.GetTableByIdAsync(table.Id, It.IsAny<CancellationToken>())).ReturnsAsync(new TableWithVersion(table, 1));
        AllowRelease(table.Id, domainEvent.ReservationId, stillHeld: true);

        await _sut.HandleAsync(domainEvent, CancellationToken.None);

        table.Status.Should().Be(TableStatus.Occupied, because: "BR-05's 'unless another active reservation holds it' clause.");
        _tablesData.Verify(
            d => d.ChangeTableStatusAsync(It.IsAny<Table>(), It.IsAny<uint>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        _auditWriter.Verify(a => a.WriteAsync(It.IsAny<AuditLogEntry>(), It.IsAny<CancellationToken>()), Times.Never);
        _realtimeNotifier.Verify(
            n => n.NotifyTableStatusChangedAsync(It.IsAny<TableStatusChangedNotification>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(TableStatus.NeedsCleaning)]
    [InlineData(TableStatus.Blocked)]
    [InlineData(TableStatus.Available)]
    public async Task HandleAsync_TableStatusNotReservationDriven_LeavesTableUntouched(TableStatus status)
    {
        var table = ExistingTable(status);
        var domainEvent = EventFor(table.Id);
        _tablesData.Setup(d => d.GetTableByIdAsync(table.Id, It.IsAny<CancellationToken>())).ReturnsAsync(new TableWithVersion(table, 1));
        AllowRelease(table.Id, domainEvent.ReservationId, stillHeld: false);

        await _sut.HandleAsync(domainEvent, CancellationToken.None);

        table.Status.Should().Be(status, because: "an automatic release must never clobber a staff-driven FR-06 override.");
        _tablesData.Verify(
            d => d.ChangeTableStatusAsync(It.IsAny<Table>(), It.IsAny<uint>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        _realtimeNotifier.Verify(
            n => n.NotifyTableStatusChangedAsync(It.IsAny<TableStatusChangedNotification>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_TableDoesNotExist_DoesNotThrow()
    {
        var domainEvent = EventFor(Guid.NewGuid());
        _tablesData.Setup(d => d.GetTableByIdAsync(domainEvent.TableId, It.IsAny<CancellationToken>())).ReturnsAsync((TableWithVersion?)null);

        var act = () => _sut.HandleAsync(domainEvent, CancellationToken.None);

        await act.Should().NotThrowAsync();
        _auditWriter.Verify(a => a.WriteAsync(It.IsAny<AuditLogEntry>(), It.IsAny<CancellationToken>()), Times.Never);
        _realtimeNotifier.Verify(
            n => n.NotifyTableStatusChangedAsync(It.IsAny<TableStatusChangedNotification>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_GatewayReportsVersionMismatch_ThrowsConcurrencyConflictException()
    {
        var table = ExistingTable(TableStatus.Occupied);
        var domainEvent = EventFor(table.Id);
        _tablesData.Setup(d => d.GetTableByIdAsync(table.Id, It.IsAny<CancellationToken>())).ReturnsAsync(new TableWithVersion(table, 1));
        AllowRelease(table.Id, domainEvent.ReservationId, stillHeld: false);
        _tablesData
            .Setup(d => d.ChangeTableStatusAsync(It.IsAny<Table>(), 1, It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TableSaveResult(TableSaveOutcome.VersionMismatch, table, 5, WasAlreadyProcessed: false));

        var act = () => _sut.HandleAsync(domainEvent, CancellationToken.None);

        await act.Should().ThrowAsync<ConcurrencyConflictException>();
        _auditWriter.Verify(a => a.WriteAsync(It.IsAny<AuditLogEntry>(), It.IsAny<CancellationToken>()), Times.Never);
        _realtimeNotifier.Verify(
            n => n.NotifyTableStatusChangedAsync(It.IsAny<TableStatusChangedNotification>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
