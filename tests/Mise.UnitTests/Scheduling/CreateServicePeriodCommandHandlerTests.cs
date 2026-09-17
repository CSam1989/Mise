using FluentValidation;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Mise.Modules.Scheduling.Application.CreateServicePeriod;
using Mise.Modules.Scheduling.Application.Ports;
using Mise.Modules.Scheduling.Domain;
using Mise.SharedKernel.Infrastructure;
using Moq;

namespace Mise.UnitTests.Scheduling;

public class CreateServicePeriodCommandHandlerTests
{
    private readonly Mock<ISchedulingData> _schedulingData = new();
    private readonly Mock<IAuditWriter> _auditWriter = new();
    private readonly FakeTimeProvider _timeProvider = new(DateTimeOffset.Parse("2026-09-15T18:00:00+02:00"));
    private readonly CreateServicePeriodCommandHandler _sut;

    public CreateServicePeriodCommandHandlerTests()
    {
        _sut = new CreateServicePeriodCommandHandler(
            _schedulingData.Object, _auditWriter.Object, new CreateServicePeriodCommandValidator(), _timeProvider,
            NullLogger<CreateServicePeriodCommandHandler>.Instance);
    }

    private static CreateServicePeriodCommand ValidCommand() => new(
        Guid.NewGuid(), new DateOnly(2026, 9, 17), "Lunch", new TimeOnly(12, 0), new TimeOnly(14, 30),
        EndsNextDay: false, IsClosed: false, Guid.NewGuid());

    [Fact]
    public async Task HandleAsync_LabelEmpty_ThrowsValidationExceptionAndNeverTouchesTheGatewayOrAuditWriter()
    {
        var command = ValidCommand() with { Label = "" };

        var act = () => _sut.HandleAsync(command, CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>();
        _schedulingData.Verify(
            d => d.CreateServicePeriodAsync(It.IsAny<ServicePeriod>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _auditWriter.Verify(a => a.WriteAsync(It.IsAny<AuditLogEntry>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_ValidCommand_PersistsViaGatewayAndWritesOneAuditEntry()
    {
        var command = ValidCommand();
        var servicePeriod = ServicePeriod.Create(
            Guid.NewGuid(), command.Date, command.Label, command.StartTime, command.EndTime,
            command.EndsNextDay, command.IsClosed);
        _schedulingData
            .Setup(d => d.CreateServicePeriodAsync(It.IsAny<ServicePeriod>(), command.OperationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ServicePeriodMutationResult(servicePeriod, WasAlreadyProcessed: false));

        var result = await _sut.HandleAsync(command, CancellationToken.None);

        result.Should().Be(servicePeriod.Id);
        _auditWriter.Verify(
            a => a.WriteAsync(
                It.Is<AuditLogEntry>(e =>
                    e.EntityType == "ServicePeriod" && e.EntityId == servicePeriod.Id && e.Action == "Created"
                    && e.PerformedByStaffId == command.PerformedByStaffId && e.OccurredAtUtc == _timeProvider.GetUtcNow()),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task HandleAsync_OperationIdAlreadyProcessed_ReturnsExistingIdAndSkipsTheAuditWrite()
    {
        var command = ValidCommand();
        var existing = ServicePeriod.Create(
            Guid.NewGuid(), command.Date, "Existing", new TimeOnly(9, 0), new TimeOnly(11, 0), false, false);
        _schedulingData
            .Setup(d => d.CreateServicePeriodAsync(It.IsAny<ServicePeriod>(), command.OperationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ServicePeriodMutationResult(existing, WasAlreadyProcessed: true));

        var result = await _sut.HandleAsync(command, CancellationToken.None);

        result.Should().Be(existing.Id);
        _auditWriter.Verify(a => a.WriteAsync(It.IsAny<AuditLogEntry>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
