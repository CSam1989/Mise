using FluentValidation;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Mise.Modules.Tables.Application.Ports;
using Mise.Modules.Tables.Application.UpdateTable;
using Mise.Modules.Tables.Domain;
using Mise.SharedKernel;
using Mise.SharedKernel.Infrastructure;
using Moq;

namespace Mise.UnitTests.Tables;

public class UpdateTableCommandHandlerTests
{
    private readonly Mock<ITablesData> _tablesData = new();
    private readonly Mock<ISectionsData> _sectionsData = new();
    private readonly Mock<IAuditWriter> _auditWriter = new();
    private readonly FakeTimeProvider _timeProvider = new(DateTimeOffset.Parse("2026-09-15T18:00:00+02:00"));
    private readonly UpdateTableCommandHandler _sut;

    public UpdateTableCommandHandlerTests()
    {
        _sut = new UpdateTableCommandHandler(
            _tablesData.Object, _sectionsData.Object, _auditWriter.Object, new UpdateTableCommandValidator(), _timeProvider,
            NullLogger<UpdateTableCommandHandler>.Instance);
    }

    private static Table ExistingTable() =>
        Table.Create(Guid.NewGuid(), Guid.NewGuid(), "T12", 2, 4, false, null, null);

    private static UpdateTableCommand ValidCommand(Table table, uint expectedVersion = 1) =>
        new(Guid.NewGuid(), table.Id, expectedVersion, table.SectionId, "T13", 3, 6, true, 1.0, 2.0, Guid.NewGuid());

    private void SectionExists(Guid sectionId) =>
        _sectionsData.Setup(d => d.GetSectionByIdAsync(sectionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Section.Create(sectionId, "Existing Section", 0));

    [Fact]
    public async Task HandleAsync_InvalidCommand_ThrowsValidationExceptionAndNeverTouchesTheGatewayOrAuditWriter()
    {
        var table = ExistingTable();
        var command = ValidCommand(table) with { Name = "" };

        var act = () => _sut.HandleAsync(command, CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>();
        _tablesData.Verify(d => d.GetTableByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        _auditWriter.Verify(a => a.Stage(It.IsAny<AuditLogEntry>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_TableDoesNotExist_ReturnsNull()
    {
        var table = ExistingTable();
        var command = ValidCommand(table);
        _tablesData.Setup(d => d.GetTableByIdAsync(table.Id, It.IsAny<CancellationToken>())).ReturnsAsync((TableWithVersion?)null);

        var result = await _sut.HandleAsync(command, CancellationToken.None);

        result.Should().BeNull();
        _auditWriter.Verify(a => a.Stage(It.IsAny<AuditLogEntry>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_SectionDoesNotExist_ThrowsFieldScopedValidationExceptionAndNeverTouchesTheGateway()
    {
        var table = ExistingTable();
        var command = ValidCommand(table);
        _tablesData.Setup(d => d.GetTableByIdAsync(table.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TableWithVersion(table, 1));
        _sectionsData.Setup(d => d.GetSectionByIdAsync(command.SectionId, It.IsAny<CancellationToken>())).ReturnsAsync((Section?)null);

        var act = () => _sut.HandleAsync(command, CancellationToken.None);

        var exception = await act.Should().ThrowAsync<ValidationException>();
        exception.Which.Errors.Should().ContainSingle(e => e.PropertyName == nameof(UpdateTableCommand.SectionId));
        _tablesData.Verify(
            d => d.UpdateTableAsync(It.IsAny<Table>(), It.IsAny<uint>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        _auditWriter.Verify(a => a.Stage(It.IsAny<AuditLogEntry>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_ValidCommand_UpdatesViaGatewayAndWritesOneAuditEntry()
    {
        var table = ExistingTable();
        var command = ValidCommand(table);
        SectionExists(command.SectionId);
        _tablesData.Setup(d => d.GetTableByIdAsync(table.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TableWithVersion(table, 1));
        _tablesData
            .Setup(d => d.UpdateTableAsync(It.IsAny<Table>(), command.ExpectedVersion, command.OperationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TableSaveResult(TableSaveOutcome.Saved, table, 2, WasAlreadyProcessed: false));

        var result = await _sut.HandleAsync(command, CancellationToken.None);

        result.Should().NotBeNull();
        result!.Version.Should().Be(2u);
        table.Name.Should().Be("T13", because: "the handler must call UpdateDetails on the loaded aggregate before persisting.");
        _auditWriter.Verify(
            a => a.Stage(
                It.Is<AuditLogEntry>(e => e.EntityType == "Table" && e.EntityId == table.Id && e.Action == "Updated")),
            Times.Once);
    }

    [Fact]
    public async Task HandleAsync_StaleExpectedVersion_ThrowsConcurrencyConflictExceptionAfterStagingTheEntry()
    {
        var table = ExistingTable();
        var command = ValidCommand(table, expectedVersion: 1);
        SectionExists(command.SectionId);
        _tablesData.Setup(d => d.GetTableByIdAsync(table.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TableWithVersion(table, 1));
        _tablesData
            .Setup(d => d.UpdateTableAsync(It.IsAny<Table>(), command.ExpectedVersion, command.OperationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TableSaveResult(TableSaveOutcome.VersionMismatch, table, 5, WasAlreadyProcessed: false));

        var act = () => _sut.HandleAsync(command, CancellationToken.None);

        var exception = await act.Should().ThrowAsync<ConcurrencyConflictException>(
            because: "docs/plan.md correction #5 — a stale If-Match must surface as a 409 with the current version, not a silent overwrite.");
        exception.Which.CurrentVersion.Should().Be(5u);
        exception.Which.EntityType.Should().Be("Table");
        exception.Which.EntityId.Should().Be(table.Id);
        _auditWriter.Verify(
            a => a.Stage(It.IsAny<AuditLogEntry>()),
            Times.Once,
            "Stage is called before the gateway call — nothing is ever actually flushed for this path.");
    }

    [Fact]
    public async Task HandleAsync_OperationIdAlreadyProcessed_StagesAuditEntryRegardlessButGatewayNeverFlushesIt()
    {
        var table = ExistingTable();
        var command = ValidCommand(table);
        SectionExists(command.SectionId);
        _tablesData.Setup(d => d.GetTableByIdAsync(table.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TableWithVersion(table, 1));
        _tablesData
            .Setup(d => d.UpdateTableAsync(It.IsAny<Table>(), command.ExpectedVersion, command.OperationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TableSaveResult(TableSaveOutcome.Saved, table, 1, WasAlreadyProcessed: true));

        await _sut.HandleAsync(command, CancellationToken.None);

        _auditWriter.Verify(
            a => a.Stage(It.IsAny<AuditLogEntry>()),
            Times.Once,
            "Stage is called unconditionally before the gateway call — see CreateReservationCommandHandlerTests' " +
            "identical replay test for the full rationale.");
    }
}
