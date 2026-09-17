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
    public async Task HandleAsync_ServicePeriodDoesNotExist_ReturnsNullAndSkipsTheAuditWrite()
    {
        var command = CommandFor(Guid.NewGuid());
        _schedulingData
            .Setup(d => d.DeleteServicePeriodAsync(command.ServicePeriodId, command.OperationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ServicePeriodDeletionResult(Existed: false, WasAlreadyProcessed: false));

        var result = await _sut.HandleAsync(command, CancellationToken.None);

        result.Should().BeNull();
        _auditWriter.Verify(a => a.WriteAsync(It.IsAny<AuditLogEntry>(), It.IsAny<CancellationToken>()), Times.Never);
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
            a => a.WriteAsync(
                It.Is<AuditLogEntry>(e =>
                    e.EntityType == "ServicePeriod" && e.EntityId == command.ServicePeriodId && e.Action == "Deleted"),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task HandleAsync_OperationIdAlreadyProcessed_ReturnsTrueAndSkipsTheAuditWrite()
    {
        var command = CommandFor(Guid.NewGuid());
        _schedulingData
            .Setup(d => d.DeleteServicePeriodAsync(command.ServicePeriodId, command.OperationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ServicePeriodDeletionResult(Existed: true, WasAlreadyProcessed: true));

        var result = await _sut.HandleAsync(command, CancellationToken.None);

        result.Should().BeTrue(
            because: "a replayed delete of an already-deleted row must still answer 204, not 404 (OperationId idempotency).");
        _auditWriter.Verify(a => a.WriteAsync(It.IsAny<AuditLogEntry>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
