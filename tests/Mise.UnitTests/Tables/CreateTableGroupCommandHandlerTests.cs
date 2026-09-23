using FluentValidation;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Mise.Modules.Tables.Application.CreateTableGroup;
using Mise.Modules.Tables.Application.Ports;
using Mise.Modules.Tables.Domain;
using Mise.SharedKernel.Infrastructure;
using Moq;

namespace Mise.UnitTests.Tables;

public class CreateTableGroupCommandHandlerTests
{
    private readonly Mock<ITableGroupsData> _tableGroupsData = new();
    private readonly Mock<ITablesData> _tablesData = new();
    private readonly Mock<IAuditWriter> _auditWriter = new();
    private readonly FakeTimeProvider _timeProvider = new(DateTimeOffset.Parse("2026-09-15T18:00:00+02:00"));
    private readonly CreateTableGroupCommandHandler _sut;

    public CreateTableGroupCommandHandlerTests()
    {
        _sut = new CreateTableGroupCommandHandler(
            _tableGroupsData.Object, _tablesData.Object, _auditWriter.Object, new CreateTableGroupCommandValidator(),
            _timeProvider, NullLogger<CreateTableGroupCommandHandler>.Instance);
        _tableGroupsData
            .Setup(d => d.GetTableIdsAlreadyInAnActiveGroupAsync(It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _tableGroupsData
            .Setup(d => d.FindExistingTableGroupIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid?)null);
    }

    private static Table CombinableTable(Guid id) =>
        Table.Create(id, Guid.NewGuid(), $"T-{id:N}", 2, 4, isCombinable: true, null, null);

    private void TableExists(Table table) =>
        _tablesData.Setup(d => d.GetTableByIdAsync(table.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TableWithVersion(table, 1));

    [Fact]
    public async Task HandleAsync_InvalidCommand_ThrowsValidationExceptionAndNeverTouchesTheGateway()
    {
        var command = new CreateTableGroupCommand(Guid.NewGuid(), "", [Guid.NewGuid(), Guid.NewGuid()], Guid.NewGuid());

        var act = () => _sut.HandleAsync(command, CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>();
        _tableGroupsData.Verify(
            d => d.CreateTableGroupAsync(It.IsAny<TableGroup>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_TableIdDoesNotExist_ThrowsValidationException()
    {
        var tableA = CombinableTable(Guid.NewGuid());
        var missingId = Guid.NewGuid();
        TableExists(tableA);
        _tablesData.Setup(d => d.GetTableByIdAsync(missingId, It.IsAny<CancellationToken>())).ReturnsAsync((TableWithVersion?)null);
        var command = new CreateTableGroupCommand(Guid.NewGuid(), "T1+T2", [tableA.Id, missingId], Guid.NewGuid());

        var act = () => _sut.HandleAsync(command, CancellationToken.None);

        var exception = await act.Should().ThrowAsync<ValidationException>();
        exception.Which.Errors.Should().ContainSingle(e => e.PropertyName == nameof(CreateTableGroupCommand.TableIds));
    }

    [Fact]
    public async Task HandleAsync_TableNotMarkedCombinable_ThrowsValidationException()
    {
        var tableA = CombinableTable(Guid.NewGuid());
        var nonCombinable = Table.Create(Guid.NewGuid(), Guid.NewGuid(), "T-Solo", 2, 4, isCombinable: false, null, null);
        TableExists(tableA);
        TableExists(nonCombinable);
        var command = new CreateTableGroupCommand(Guid.NewGuid(), "T1+T2", [tableA.Id, nonCombinable.Id], Guid.NewGuid());

        var act = () => _sut.HandleAsync(command, CancellationToken.None);

        var exception = await act.Should().ThrowAsync<ValidationException>();
        exception.Which.Errors.Should().ContainSingle(e => e.PropertyName == nameof(CreateTableGroupCommand.TableIds))
            .Which.ErrorMessage.Should().Contain("not marked combinable");
    }

    [Fact]
    public async Task HandleAsync_TableAlreadyInAnActiveGroup_ThrowsValidationException()
    {
        var tableA = CombinableTable(Guid.NewGuid());
        var tableB = CombinableTable(Guid.NewGuid());
        TableExists(tableA);
        TableExists(tableB);
        _tableGroupsData
            .Setup(d => d.GetTableIdsAlreadyInAnActiveGroupAsync(It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([tableA.Id]);
        var command = new CreateTableGroupCommand(Guid.NewGuid(), "T1+T2", [tableA.Id, tableB.Id], Guid.NewGuid());

        var act = () => _sut.HandleAsync(command, CancellationToken.None);

        var exception = await act.Should().ThrowAsync<ValidationException>();
        exception.Which.Errors.Should().ContainSingle(e => e.PropertyName == nameof(CreateTableGroupCommand.TableIds))
            .Which.ErrorMessage.Should().Contain("already belongs to another active table group");
        _tableGroupsData.Verify(
            d => d.CreateTableGroupAsync(It.IsAny<TableGroup>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_ValidCommand_CreatesViaGatewayAndWritesOneAuditEntry()
    {
        var tableA = CombinableTable(Guid.NewGuid());
        var tableB = CombinableTable(Guid.NewGuid());
        TableExists(tableA);
        TableExists(tableB);
        var command = new CreateTableGroupCommand(Guid.NewGuid(), "T1+T2", [tableA.Id, tableB.Id], Guid.NewGuid());
        var groupId = Guid.NewGuid();
        _tableGroupsData
            .Setup(d => d.CreateTableGroupAsync(It.IsAny<TableGroup>(), command.OperationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TableGroupCreateResult(groupId, WasAlreadyProcessed: false));

        var result = await _sut.HandleAsync(command, CancellationToken.None);

        result.TableGroupId.Should().Be(groupId);
        _auditWriter.Verify(
            a => a.Stage(It.Is<AuditLogEntry>(e => e.EntityType == "TableGroup" && e.Action == "Created")),
            Times.Once);
    }

    [Fact]
    public async Task HandleAsync_OperationIdAlreadyProcessed_ShortCircuitsBeforeAnyValidationAndSkipsTheAuditWrite()
    {
        // The regression this guards: on a genuine replay, the tables named in the command are
        // now already grouped — the exact state the first, real run produced. If the idempotency
        // check ran after the "not already grouped" validation instead of before it, this replay
        // would incorrectly fail with a 400 (same pitfall CLAUDE.md's Scheduling section already
        // documents for hard deletes). Deliberately never sets up ITablesData/
        // GetTableIdsAlreadyInAnActiveGroupAsync for this command's table ids, so the Verify
        // calls below prove none of that validation ran at all, not just that it happened not to fail.
        var existingGroupId = Guid.NewGuid();
        var command = new CreateTableGroupCommand(Guid.NewGuid(), "T1+T2", [Guid.NewGuid(), Guid.NewGuid()], Guid.NewGuid());
        _tableGroupsData
            .Setup(d => d.FindExistingTableGroupIdAsync(command.OperationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingGroupId);

        var result = await _sut.HandleAsync(command, CancellationToken.None);

        result.Should().Be(new TableGroupCreateResult(existingGroupId, WasAlreadyProcessed: true));
        _tablesData.Verify(d => d.GetTableByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        _tableGroupsData.Verify(
            d => d.GetTableIdsAlreadyInAnActiveGroupAsync(It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()), Times.Never);
        _tableGroupsData.Verify(
            d => d.CreateTableGroupAsync(It.IsAny<TableGroup>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        _auditWriter.Verify(
            a => a.Stage(It.IsAny<AuditLogEntry>()),
            Times.Never,
            "unlike every other handler, this idempotency check runs before TableGroup.Create/Stage are ever " +
            "reached (the whole point of checking it first — see the handler's own doc comment), so Stage is " +
            "never even called on this path, not just never flushed.");
    }
}
