using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Mise.Modules.Tables.Application.DeactivateTable;
using Mise.Modules.Tables.Application.Ports;
using Mise.Modules.Tables.Domain;
using Mise.SharedKernel;
using Mise.SharedKernel.Infrastructure;
using Moq;

namespace Mise.UnitTests.Tables;

public class DeactivateTableCommandHandlerTests
{
    private readonly Mock<ITablesData> _tablesData = new();
    private readonly Mock<IAuditWriter> _auditWriter = new();
    private readonly FakeTimeProvider _timeProvider = new(DateTimeOffset.Parse("2026-09-15T18:00:00+02:00"));
    private readonly DeactivateTableCommandHandler _sut;

    public DeactivateTableCommandHandlerTests()
    {
        _sut = new DeactivateTableCommandHandler(
            _tablesData.Object, _auditWriter.Object, _timeProvider, NullLogger<DeactivateTableCommandHandler>.Instance);
    }

    private static Table ExistingTable() => Table.Create(Guid.NewGuid(), Guid.NewGuid(), "T12", 2, 4, false, null, null);

    private static DeactivateTableCommand CommandFor(Table table, uint expectedVersion = 1) =>
        new(Guid.NewGuid(), table.Id, expectedVersion, Guid.NewGuid());

    [Fact]
    public async Task HandleAsync_TableDoesNotExist_ReturnsNull()
    {
        var table = ExistingTable();
        var command = CommandFor(table);
        _tablesData.Setup(d => d.GetTableByIdAsync(table.Id, It.IsAny<CancellationToken>())).ReturnsAsync((TableWithVersion?)null);

        var result = await _sut.HandleAsync(command, CancellationToken.None);

        result.Should().BeNull();
        _auditWriter.Verify(a => a.Stage(It.IsAny<AuditLogEntry>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_ValidCommand_DeactivatesViaGatewayAndWritesOneAuditEntry()
    {
        var table = ExistingTable();
        var command = CommandFor(table);
        _tablesData.Setup(d => d.GetTableByIdAsync(table.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TableWithVersion(table, 1));
        _tablesData
            .Setup(d => d.DeactivateTableAsync(It.IsAny<Table>(), command.ExpectedVersion, command.OperationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TableSaveResult(TableSaveOutcome.Saved, table, 2, WasAlreadyProcessed: false));

        var result = await _sut.HandleAsync(command, CancellationToken.None);

        result.Should().NotBeNull();
        table.IsActive.Should().BeFalse();
        _auditWriter.Verify(
            a => a.Stage(
                It.Is<AuditLogEntry>(e => e.EntityType == "Table" && e.EntityId == table.Id && e.Action == "Deactivated")),
            Times.Once);
    }

    [Fact]
    public async Task HandleAsync_TableIsOccupied_ThrowsDomainRuleViolationExceptionAndNeverTouchesTheGateway()
    {
        var table = ExistingTable();
        typeof(Table).GetProperty(nameof(Table.Status))!.SetValue(table, TableStatus.Occupied);
        var command = CommandFor(table);
        _tablesData.Setup(d => d.GetTableByIdAsync(table.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TableWithVersion(table, 1));

        var act = () => _sut.HandleAsync(command, CancellationToken.None);

        await act.Should().ThrowAsync<DomainRuleViolationException>(
            because: "docs/plan.md correction #13 — deactivating an Occupied table is blocked before the gateway is ever called.");
        _tablesData.Verify(
            d => d.DeactivateTableAsync(It.IsAny<Table>(), It.IsAny<uint>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _auditWriter.Verify(a => a.Stage(It.IsAny<AuditLogEntry>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_StaleExpectedVersion_ThrowsConcurrencyConflictExceptionAfterStagingTheEntry()
    {
        var table = ExistingTable();
        var command = CommandFor(table);
        _tablesData.Setup(d => d.GetTableByIdAsync(table.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TableWithVersion(table, 1));
        _tablesData
            .Setup(d => d.DeactivateTableAsync(It.IsAny<Table>(), command.ExpectedVersion, command.OperationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TableSaveResult(TableSaveOutcome.VersionMismatch, table, 5, WasAlreadyProcessed: false));

        var act = () => _sut.HandleAsync(command, CancellationToken.None);

        var exception = await act.Should().ThrowAsync<ConcurrencyConflictException>();
        exception.Which.CurrentVersion.Should().Be(5u);
        _auditWriter.Verify(
            a => a.Stage(It.IsAny<AuditLogEntry>()),
            Times.Once,
            "Stage is called before the gateway call — nothing is ever actually flushed for this path.");
    }

    [Fact]
    public async Task HandleAsync_OperationIdAlreadyProcessed_StagesAuditEntryRegardlessButGatewayNeverFlushesIt()
    {
        var table = ExistingTable();
        var command = CommandFor(table);
        _tablesData.Setup(d => d.GetTableByIdAsync(table.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TableWithVersion(table, 1));
        _tablesData
            .Setup(d => d.DeactivateTableAsync(It.IsAny<Table>(), command.ExpectedVersion, command.OperationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TableSaveResult(TableSaveOutcome.Saved, table, 1, WasAlreadyProcessed: true));

        await _sut.HandleAsync(command, CancellationToken.None);

        _auditWriter.Verify(
            a => a.Stage(It.IsAny<AuditLogEntry>()),
            Times.Once,
            "Stage is called unconditionally before the gateway call — see CreateReservationCommandHandlerTests' " +
            "identical replay test for the full rationale.");
    }
}
