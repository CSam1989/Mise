using FluentValidation;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Mise.Modules.Reservations.Application.CreateReservation;
using Mise.Modules.Reservations.Application.Ports;
using Mise.Modules.Reservations.Domain;
using Mise.SharedKernel.Infrastructure;
using Moq;

namespace Mise.UnitTests.Reservations;

public class CreateReservationCommandHandlerTests
{
    private readonly Mock<IReservationsData> _reservationsData = new();
    private readonly Mock<IAuditWriter> _auditWriter = new();
    private readonly FakeTimeProvider _timeProvider = new(DateTimeOffset.Parse("2026-09-15T18:00:00+02:00"));
    private readonly CreateReservationCommandHandler _sut;

    public CreateReservationCommandHandlerTests()
    {
        _sut = new CreateReservationCommandHandler(
            _reservationsData.Object,
            _auditWriter.Object,
            new CreateReservationCommandValidator(),
            _timeProvider,
            NullLogger<CreateReservationCommandHandler>.Instance);
    }

    private static readonly Guid PerformingStaffId = Guid.NewGuid();

    private static CreateReservationCommand ValidCommand(int partySize = 4) =>
        new(Guid.NewGuid(), "Jane Doe", partySize, DateTimeOffset.Parse("2026-09-20T19:00:00+02:00"), PerformingStaffId);

    [Fact]
    public async Task HandleAsync_PartySizeZero_ThrowsValidationExceptionAndNeverTouchesTheGatewayOrAuditWriter()
    {
        var command = ValidCommand(partySize: 0);

        var act = () => _sut.HandleAsync(command, CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>(
            because: "the handler validates before touching the gateway (CLAUDE.md's handler test contract).");
        _reservationsData.Verify(
            d => d.CreateReservationAsync(It.IsAny<Reservation>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _auditWriter.Verify(
            a => a.WriteAsync(It.IsAny<AuditLogEntry>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task HandleAsync_ValidCommand_PersistsViaGatewayAndWritesOneAuditEntry()
    {
        var command = ValidCommand();
        var reservationId = Guid.NewGuid();
        _reservationsData
            .Setup(d => d.CreateReservationAsync(It.IsAny<Reservation>(), command.OperationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CreateReservationResult(reservationId, WasAlreadyProcessed: false));

        var result = await _sut.HandleAsync(command, CancellationToken.None);

        result.Should().Be(reservationId,
            because: "the handler returns whatever id the gateway reports as the persisted reservation's id.");
        _reservationsData.Verify(
            d => d.CreateReservationAsync(
                It.Is<Reservation>(r => r.CustomerName == command.CustomerName && r.PartySize == command.PartySize),
                command.OperationId,
                It.IsAny<CancellationToken>()),
            Times.Once);
        _auditWriter.Verify(
            a => a.WriteAsync(
                It.Is<AuditLogEntry>(e =>
                    e.EntityId == reservationId
                    && e.Action == "Created"
                    && e.PerformedByStaffId == command.PerformedByStaffId
                    && e.OccurredAtUtc == _timeProvider.GetUtcNow()),
                It.IsAny<CancellationToken>()),
            Times.Once,
            "the audit entry's timestamp must come from the injected TimeProvider, never the wall clock.");
    }

    [Fact]
    public async Task HandleAsync_OperationIdAlreadyProcessed_ReturnsExistingIdAndSkipsTheAuditWrite()
    {
        var command = ValidCommand();
        var existingReservationId = Guid.NewGuid();
        _reservationsData
            .Setup(d => d.CreateReservationAsync(It.IsAny<Reservation>(), command.OperationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CreateReservationResult(existingReservationId, WasAlreadyProcessed: true));

        var result = await _sut.HandleAsync(command, CancellationToken.None);

        result.Should().Be(existingReservationId,
            because: "a replayed OperationId returns the id of the reservation it originally created (docs/plan.md's OperationId replay contract).");
        _auditWriter.Verify(
            a => a.WriteAsync(It.IsAny<AuditLogEntry>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "replaying an already-processed operation must not write a second audit entry for the same effect.");
    }
}
