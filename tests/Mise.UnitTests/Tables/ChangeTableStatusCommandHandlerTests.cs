using FluentValidation;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Mise.Modules.Tables.Application.ChangeTableStatus;
using Mise.Modules.Tables.Application.Ports;
using Mise.Modules.Tables.Domain;
using Mise.SharedKernel;
using Mise.SharedKernel.Infrastructure;
using Moq;

namespace Mise.UnitTests.Tables;

public class ChangeTableStatusCommandHandlerTests
{
    private readonly Mock<ITablesData> _tablesData = new();
    private readonly Mock<IAuditWriter> _auditWriter = new();
    private readonly Mock<IRealtimeNotifier> _realtimeNotifier = new();
    private readonly FakeTimeProvider _timeProvider = new(DateTimeOffset.Parse("2026-09-15T18:00:00+02:00"));
    private readonly ChangeTableStatusCommandHandler _sut;

    public ChangeTableStatusCommandHandlerTests()
    {
        _sut = new ChangeTableStatusCommandHandler(
            _tablesData.Object, _auditWriter.Object, _realtimeNotifier.Object, new ChangeTableStatusCommandValidator(), _timeProvider,
            NullLogger<ChangeTableStatusCommandHandler>.Instance);
    }

    private static Table ExistingTable() => Table.Create(Guid.NewGuid(), Guid.NewGuid(), "T12", 2, 4, false, null, null);

    private static ChangeTableStatusCommand CommandFor(Table table, string status = "Occupied", uint expectedVersion = 1) =>
        new(Guid.NewGuid(), table.Id, expectedVersion, status, Guid.NewGuid());

    [Fact]
    public async Task HandleAsync_InvalidStatus_ThrowsValidationExceptionAndNeverTouchesTheGateway()
    {
        var table = ExistingTable();
        var command = CommandFor(table, status: "OnFire");

        var act = () => _sut.HandleAsync(command, CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>();
        _tablesData.Verify(d => d.GetTableByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_TableDoesNotExist_ReturnsNull()
    {
        var table = ExistingTable();
        var command = CommandFor(table);
        _tablesData.Setup(d => d.GetTableByIdAsync(table.Id, It.IsAny<CancellationToken>())).ReturnsAsync((TableWithVersion?)null);

        var result = await _sut.HandleAsync(command, CancellationToken.None);

        result.Should().BeNull();
        _auditWriter.Verify(a => a.WriteAsync(It.IsAny<AuditLogEntry>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_ValidCommand_ChangesStatusViaGatewayAndWritesOneAuditEntry()
    {
        var table = ExistingTable();
        var command = CommandFor(table, status: "NeedsCleaning");
        _tablesData.Setup(d => d.GetTableByIdAsync(table.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TableWithVersion(table, 1));
        _tablesData
            .Setup(d => d.ChangeTableStatusAsync(It.IsAny<Table>(), command.ExpectedVersion, command.OperationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TableSaveResult(TableSaveOutcome.Saved, table, 2, WasAlreadyProcessed: false));

        var result = await _sut.HandleAsync(command, CancellationToken.None);

        result.Should().NotBeNull();
        table.Status.Should().Be(TableStatus.NeedsCleaning, because: "the handler must call SetStatus on the loaded aggregate before persisting.");
        _auditWriter.Verify(
            a => a.WriteAsync(
                It.Is<AuditLogEntry>(e => e.EntityType == "Table" && e.EntityId == table.Id && e.Action == "StatusChanged" && e.Details == "NeedsCleaning"),
                It.IsAny<CancellationToken>()),
            Times.Once);
        _realtimeNotifier.Verify(
            n => n.NotifyTableStatusChangedAsync(
                It.Is<TableStatusChangedNotification>(p => p.TableId == table.Id && p.Status == "NeedsCleaning"), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task HandleAsync_StaleExpectedVersion_ThrowsConcurrencyConflictExceptionAndSkipsTheAuditWrite()
    {
        var table = ExistingTable();
        var command = CommandFor(table);
        _tablesData.Setup(d => d.GetTableByIdAsync(table.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TableWithVersion(table, 1));
        _tablesData
            .Setup(d => d.ChangeTableStatusAsync(It.IsAny<Table>(), command.ExpectedVersion, command.OperationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TableSaveResult(TableSaveOutcome.VersionMismatch, table, 5, WasAlreadyProcessed: false));

        var act = () => _sut.HandleAsync(command, CancellationToken.None);

        var exception = await act.Should().ThrowAsync<ConcurrencyConflictException>();
        exception.Which.CurrentVersion.Should().Be(5u);
        _auditWriter.Verify(a => a.WriteAsync(It.IsAny<AuditLogEntry>(), It.IsAny<CancellationToken>()), Times.Never);
        _realtimeNotifier.Verify(
            n => n.NotifyTableStatusChangedAsync(It.IsAny<TableStatusChangedNotification>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_OperationIdAlreadyProcessed_SkipsTheAuditWrite()
    {
        var table = ExistingTable();
        var command = CommandFor(table);
        _tablesData.Setup(d => d.GetTableByIdAsync(table.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TableWithVersion(table, 1));
        _tablesData
            .Setup(d => d.ChangeTableStatusAsync(It.IsAny<Table>(), command.ExpectedVersion, command.OperationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TableSaveResult(TableSaveOutcome.Saved, table, 1, WasAlreadyProcessed: true));

        await _sut.HandleAsync(command, CancellationToken.None);

        _auditWriter.Verify(a => a.WriteAsync(It.IsAny<AuditLogEntry>(), It.IsAny<CancellationToken>()), Times.Never);
        _realtimeNotifier.Verify(
            n => n.NotifyTableStatusChangedAsync(It.IsAny<TableStatusChangedNotification>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
