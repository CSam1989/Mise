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

public class ReservationSeatedTableOccupiedHandlerTests
{
    private readonly Mock<ITablesData> _tablesData = new();
    private readonly Mock<IAuditWriter> _auditWriter = new();
    private readonly Mock<IRealtimeNotifier> _realtimeNotifier = new();
    private readonly FakeTimeProvider _timeProvider = new(DateTimeOffset.Parse("2026-09-15T18:00:00+02:00"));
    private readonly ReservationSeatedTableOccupiedHandler _sut;

    public ReservationSeatedTableOccupiedHandlerTests()
    {
        _sut = new ReservationSeatedTableOccupiedHandler(
            _tablesData.Object, _auditWriter.Object, _realtimeNotifier.Object, _timeProvider,
            NullLogger<ReservationSeatedTableOccupiedHandler>.Instance);
    }

    private static Table ExistingTable() => Table.Create(Guid.NewGuid(), Guid.NewGuid(), "T12", 2, 4, false, null, null);

    private static ReservationSeated EventFor(Guid tableId) =>
        new(Guid.NewGuid(), tableId, Guid.NewGuid(), DateTimeOffset.Parse("2026-09-15T18:00:00+02:00"));

    [Fact]
    public async Task HandleAsync_TableExistsAndAvailable_MarksOccupiedAndWritesOneAuditEntry()
    {
        var table = ExistingTable();
        var domainEvent = EventFor(table.Id);
        _tablesData.Setup(d => d.GetTableByIdAsync(table.Id, It.IsAny<CancellationToken>())).ReturnsAsync(new TableWithVersion(table, 1));
        _tablesData
            .Setup(d => d.ChangeTableStatusAsync(It.IsAny<Table>(), 1, It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TableSaveResult(TableSaveOutcome.Saved, table, 2, WasAlreadyProcessed: false));

        await _sut.HandleAsync(domainEvent, CancellationToken.None);

        table.Status.Should().Be(TableStatus.Occupied);
        _auditWriter.Verify(
            a => a.WriteAsync(It.Is<AuditLogEntry>(e => e.EntityType == "Table" && e.EntityId == table.Id && e.Action == "Occupied"), It.IsAny<CancellationToken>()),
            Times.Once);
        _realtimeNotifier.Verify(
            n => n.NotifyTableStatusChangedAsync(
                It.Is<TableStatusChangedNotification>(p => p.TableId == table.Id && p.Status == "Occupied"), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task HandleAsync_TableAlreadyOccupied_SkipsThePersistAndAuditWrite()
    {
        var table = ExistingTable();
        table.SetStatus(TableStatus.Occupied);
        var domainEvent = EventFor(table.Id);
        _tablesData.Setup(d => d.GetTableByIdAsync(table.Id, It.IsAny<CancellationToken>())).ReturnsAsync(new TableWithVersion(table, 1));

        await _sut.HandleAsync(domainEvent, CancellationToken.None);

        _tablesData.Verify(
            d => d.ChangeTableStatusAsync(It.IsAny<Table>(), It.IsAny<uint>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never,
            "a replayed event that already applied cleanly must not force a redundant write.");
        _auditWriter.Verify(a => a.WriteAsync(It.IsAny<AuditLogEntry>(), It.IsAny<CancellationToken>()), Times.Never);
        _realtimeNotifier.Verify(
            n => n.NotifyTableStatusChangedAsync(It.IsAny<TableStatusChangedNotification>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_TableDoesNotExist_DoesNotThrow()
    {
        var domainEvent = EventFor(Guid.NewGuid());
        _tablesData.Setup(d => d.GetTableByIdAsync(domainEvent.TableId, It.IsAny<CancellationToken>())).ReturnsAsync((TableWithVersion?)null);

        var act = () => _sut.HandleAsync(domainEvent, CancellationToken.None);

        await act.Should().NotThrowAsync(because: "a genuinely impossible race (the table existed moments earlier in the same request) is logged, not retried.");
        _auditWriter.Verify(a => a.WriteAsync(It.IsAny<AuditLogEntry>(), It.IsAny<CancellationToken>()), Times.Never);
        _realtimeNotifier.Verify(
            n => n.NotifyTableStatusChangedAsync(It.IsAny<TableStatusChangedNotification>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_GatewayReportsVersionMismatch_ThrowsConcurrencyConflictException()
    {
        var table = ExistingTable();
        var domainEvent = EventFor(table.Id);
        _tablesData.Setup(d => d.GetTableByIdAsync(table.Id, It.IsAny<CancellationToken>())).ReturnsAsync(new TableWithVersion(table, 1));
        _tablesData
            .Setup(d => d.ChangeTableStatusAsync(It.IsAny<Table>(), 1, It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TableSaveResult(TableSaveOutcome.VersionMismatch, table, 5, WasAlreadyProcessed: false));

        var act = () => _sut.HandleAsync(domainEvent, CancellationToken.None);

        await act.Should().ThrowAsync<ConcurrencyConflictException>(
            because: "unlike the not-found case, this is transient and retry-recoverable — it must propagate so a client retry can heal it.");
        _auditWriter.Verify(a => a.WriteAsync(It.IsAny<AuditLogEntry>(), It.IsAny<CancellationToken>()), Times.Never);
        _realtimeNotifier.Verify(
            n => n.NotifyTableStatusChangedAsync(It.IsAny<TableStatusChangedNotification>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
