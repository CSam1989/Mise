using FluentValidation;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Mise.Modules.Tables.Application.CreateTable;
using Mise.Modules.Tables.Application.Ports;
using Mise.Modules.Tables.Domain;
using Mise.SharedKernel.Infrastructure;
using Moq;

namespace Mise.UnitTests.Tables;

public class CreateTableCommandHandlerTests
{
    private readonly Mock<ITablesData> _tablesData = new();
    private readonly Mock<ISectionsData> _sectionsData = new();
    private readonly Mock<IAuditWriter> _auditWriter = new();
    private readonly FakeTimeProvider _timeProvider = new(DateTimeOffset.Parse("2026-09-15T18:00:00+02:00"));
    private readonly CreateTableCommandHandler _sut;

    public CreateTableCommandHandlerTests()
    {
        _sut = new CreateTableCommandHandler(
            _tablesData.Object, _sectionsData.Object, _auditWriter.Object, new CreateTableCommandValidator(), _timeProvider,
            NullLogger<CreateTableCommandHandler>.Instance);
    }

    private static CreateTableCommand ValidCommand() =>
        new(Guid.NewGuid(), Guid.NewGuid(), "T12", 2, 4, false, null, null, Guid.NewGuid());

    private void SectionExists(Guid sectionId) =>
        _sectionsData.Setup(d => d.GetSectionByIdAsync(sectionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Section.Create(sectionId, "Existing Section", 0));

    [Fact]
    public async Task HandleAsync_InvalidCommand_ThrowsValidationExceptionAndNeverTouchesTheGatewayOrAuditWriter()
    {
        var command = ValidCommand() with { Name = "" };

        var act = () => _sut.HandleAsync(command, CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>();
        _tablesData.Verify(
            d => d.CreateTableAsync(It.IsAny<Table>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        _auditWriter.Verify(a => a.Stage(It.IsAny<AuditLogEntry>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_SectionDoesNotExist_ThrowsFieldScopedValidationExceptionAndNeverTouchesTheGateway()
    {
        var command = ValidCommand();
        _sectionsData.Setup(d => d.GetSectionByIdAsync(command.SectionId, It.IsAny<CancellationToken>())).ReturnsAsync((Section?)null);

        var act = () => _sut.HandleAsync(command, CancellationToken.None);

        var exception = await act.Should().ThrowAsync<ValidationException>(
            because: "a SectionId with no matching row must be a clean 400, not a raw FK-violation 500.");
        exception.Which.Errors.Should().ContainSingle(e => e.PropertyName == nameof(CreateTableCommand.SectionId));
        _tablesData.Verify(
            d => d.CreateTableAsync(It.IsAny<Table>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        _auditWriter.Verify(a => a.Stage(It.IsAny<AuditLogEntry>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_ValidCommand_PersistsViaGatewayAndWritesOneAuditEntry()
    {
        var command = ValidCommand();
        SectionExists(command.SectionId);
        var tableId = Guid.NewGuid();
        Table? passedTable = null;
        _tablesData
            .Setup(d => d.CreateTableAsync(It.IsAny<Table>(), command.OperationId, It.IsAny<CancellationToken>()))
            .Callback<Table, Guid, CancellationToken>((t, _, _) => passedTable = t)
            .ReturnsAsync(new TableCreateResult(tableId, Version: 1, WasAlreadyProcessed: false));

        var result = await _sut.HandleAsync(command, CancellationToken.None);

        result.TableId.Should().Be(tableId);
        result.Version.Should().Be(1u, because: "the endpoint needs the freshly assigned version to set the response's ETag header.");
        _tablesData.Verify(
            d => d.CreateTableAsync(
                It.Is<Table>(t => t.Name == command.Name && t.MinCapacity == command.MinCapacity),
                command.OperationId, It.IsAny<CancellationToken>()),
            Times.Once);
        _auditWriter.Verify(
            a => a.Stage(
                It.Is<AuditLogEntry>(e =>
                    e.EntityType == "Table" && e.EntityId == passedTable!.Id && e.Action == "Created"
                    && e.PerformedByStaffId == command.PerformedByStaffId && e.OccurredAtUtc == _timeProvider.GetUtcNow())),
            Times.Once,
            "EntityId is the handler's own client-generated Table.Id, staged before the gateway call — not the " +
            "mocked gateway result's (unrelated) tableId.");
    }

    [Fact]
    public async Task HandleAsync_OperationIdAlreadyProcessed_ReturnsExistingResultAndStagesAuditEntryRegardless()
    {
        var command = ValidCommand();
        SectionExists(command.SectionId);
        var existingTableId = Guid.NewGuid();
        _tablesData
            .Setup(d => d.CreateTableAsync(It.IsAny<Table>(), command.OperationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TableCreateResult(existingTableId, Version: 3, WasAlreadyProcessed: true));

        var result = await _sut.HandleAsync(command, CancellationToken.None);

        result.TableId.Should().Be(existingTableId);
        _auditWriter.Verify(
            a => a.Stage(It.IsAny<AuditLogEntry>()),
            Times.Once,
            "Stage is called unconditionally before the gateway call — see CreateReservationCommandHandlerTests' " +
            "identical replay test for the full rationale.");
    }
}
