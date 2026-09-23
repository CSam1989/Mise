using FluentValidation;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Mise.Modules.Scheduling.Application.Ports;
using Mise.Modules.Scheduling.Application.UpdateServicePeriod;
using Mise.Modules.Scheduling.Domain;
using Mise.SharedKernel.Infrastructure;
using Moq;

namespace Mise.UnitTests.Scheduling;

public class UpdateServicePeriodCommandHandlerTests
{
    private readonly Mock<ISchedulingData> _schedulingData = new();
    private readonly Mock<IAuditWriter> _auditWriter = new();
    private readonly FakeTimeProvider _timeProvider = new(DateTimeOffset.Parse("2026-09-15T18:00:00+02:00"));
    private readonly UpdateServicePeriodCommandHandler _sut;

    public UpdateServicePeriodCommandHandlerTests()
    {
        _sut = new UpdateServicePeriodCommandHandler(
            _schedulingData.Object, _auditWriter.Object, new UpdateServicePeriodCommandValidator(), _timeProvider,
            NullLogger<UpdateServicePeriodCommandHandler>.Instance);
    }

    private static UpdateServicePeriodCommand CommandFor(Guid servicePeriodId) => new(
        Guid.NewGuid(), servicePeriodId, new DateOnly(2026, 9, 17), "Brunch", new TimeOnly(11, 0), new TimeOnly(15, 0),
        EndsNextDay: false, IsClosed: false, Guid.NewGuid());

    [Fact]
    public async Task HandleAsync_LabelEmpty_ThrowsValidationExceptionAndNeverTouchesTheGateway()
    {
        var command = CommandFor(Guid.NewGuid()) with { Label = "" };

        var act = () => _sut.HandleAsync(command, CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>();
        _schedulingData.Verify(d => d.GetServicePeriodByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_ServicePeriodDoesNotExist_ReturnsNull()
    {
        var command = CommandFor(Guid.NewGuid());
        _schedulingData
            .Setup(d => d.GetServicePeriodByIdAsync(command.ServicePeriodId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ServicePeriod?)null);

        var result = await _sut.HandleAsync(command, CancellationToken.None);

        result.Should().BeNull();
        _auditWriter.Verify(a => a.Stage(It.IsAny<AuditLogEntry>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_ValidCommand_UpdatesAndWritesOneAuditEntry()
    {
        var existing = ServicePeriod.Create(
            Guid.NewGuid(), new DateOnly(2026, 9, 17), "Lunch", new TimeOnly(12, 0), new TimeOnly(14, 30), false, false);
        var command = CommandFor(existing.Id);
        _schedulingData.Setup(d => d.GetServicePeriodByIdAsync(existing.Id, It.IsAny<CancellationToken>())).ReturnsAsync(existing);
        _schedulingData
            .Setup(d => d.UpdateServicePeriodAsync(It.IsAny<ServicePeriod>(), command.OperationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ServicePeriodMutationResult(existing, WasAlreadyProcessed: false));

        var result = await _sut.HandleAsync(command, CancellationToken.None);

        result.Should().NotBeNull();
        existing.Label.Should().Be("Brunch");
        _auditWriter.Verify(
            a => a.Stage(
                It.Is<AuditLogEntry>(e => e.EntityType == "ServicePeriod" && e.EntityId == existing.Id && e.Action == "Updated")),
            Times.Once);
    }

    [Fact]
    public async Task HandleAsync_OperationIdAlreadyProcessed_StagesAuditEntryRegardlessButGatewayNeverFlushesIt()
    {
        var existing = ServicePeriod.Create(
            Guid.NewGuid(), new DateOnly(2026, 9, 17), "Lunch", new TimeOnly(12, 0), new TimeOnly(14, 30), false, false);
        var command = CommandFor(existing.Id);
        _schedulingData.Setup(d => d.GetServicePeriodByIdAsync(existing.Id, It.IsAny<CancellationToken>())).ReturnsAsync(existing);
        _schedulingData
            .Setup(d => d.UpdateServicePeriodAsync(It.IsAny<ServicePeriod>(), command.OperationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ServicePeriodMutationResult(existing, WasAlreadyProcessed: true));

        var result = await _sut.HandleAsync(command, CancellationToken.None);

        result.Should().NotBeNull();
        _auditWriter.Verify(
            a => a.Stage(It.IsAny<AuditLogEntry>()),
            Times.Once,
            "Stage is called unconditionally before the gateway call — see CreateReservationCommandHandlerTests' " +
            "identical replay test for the full rationale.");
    }
}
