using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Mise.Modules.Scheduling.Application.DeleteServicePeriod;
using Mise.Modules.Scheduling.Application.Ports;
using Mise.SharedKernel.Infrastructure;
using Moq;

namespace Mise.UnitTests.Scheduling;

public class DeleteServicePeriodCommandHandlerTests
{
    private readonly Mock<ISchedulingData> _schedulingData = new();
    private readonly Mock<IAuditWriter> _auditWriter = new();
    private readonly FakeTimeProvider _timeProvider = new(DateTimeOffset.Parse("2026-09-15T18:00:00+02:00"));
    private readonly DeleteServicePeriodCommandHandler _sut;

    public DeleteServicePeriodCommandHandlerTests()
    {
        _sut = new DeleteServicePeriodCommandHandler(
            _schedulingData.Object, _auditWriter.Object, _timeProvider, NullLogger<DeleteServicePeriodCommandHandler>.Instance);
    }

    private static DeleteServicePeriodCommand CommandFor(Guid servicePeriodId) => new(Guid.NewGuid(), servicePeriodId, Guid.NewGuid());

    [Fact]
    public async Task HandleAsync_ServicePeriodDoesNotExist_ReturnsNullButStillStagesAuditEntry()
    {
        var command = CommandFor(Guid.NewGuid());
        _schedulingData
            .Setup(d => d.DeleteServicePeriodAsync(command.ServicePeriodId, command.OperationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ServicePeriodDeletionResult(Existed: false, WasAlreadyProcessed: false));

        var result = await _sut.HandleAsync(command, CancellationToken.None);

        result.Should().BeNull();
        _auditWriter.Verify(
            a => a.Stage(It.IsAny<AuditLogEntry>()),
            Times.Once,
            "unlike Create/Update, Delete has no domain object to build first, so Stage is called unconditionally " +
            "before the ONE gateway call that also determines not-found/replay — nothing is ever actually flushed " +
            "for this path since the gateway's own SaveChangesAsync is never reached.");
    }

    [Fact]
    public async Task HandleAsync_ServicePeriodExists_DeletesAndWritesOneAuditEntry()
    {
        var command = CommandFor(Guid.NewGuid());
        _schedulingData
            .Setup(d => d.DeleteServicePeriodAsync(command.ServicePeriodId, command.OperationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ServicePeriodDeletionResult(Existed: true, WasAlreadyProcessed: false));

        var result = await _sut.HandleAsync(command, CancellationToken.None);

        result.Should().BeTrue();
        _auditWriter.Verify(
            a => a.Stage(
                It.Is<AuditLogEntry>(e =>
                    e.EntityType == "ServicePeriod" && e.EntityId == command.ServicePeriodId && e.Action == "Deleted")),
            Times.Once);
    }

    [Fact]
    public async Task HandleAsync_OperationIdAlreadyProcessed_ReturnsTrueAndStagesAuditEntryRegardless()
    {
        var command = CommandFor(Guid.NewGuid());
        _schedulingData
            .Setup(d => d.DeleteServicePeriodAsync(command.ServicePeriodId, command.OperationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ServicePeriodDeletionResult(Existed: true, WasAlreadyProcessed: true));

        var result = await _sut.HandleAsync(command, CancellationToken.None);

        result.Should().BeTrue(
            because: "a replayed delete of an already-deleted row must still answer 204, not 404 (OperationId idempotency).");
        _auditWriter.Verify(
            a => a.Stage(It.IsAny<AuditLogEntry>()),
            Times.Once,
            "Stage is called unconditionally before the single gateway call — see CreateReservationCommandHandlerTests' " +
            "identical replay test for the full rationale.");
    }
}
