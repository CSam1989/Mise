using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Mise.Modules.Reservations.Application.CancelReservation;
using Mise.Modules.Reservations.Application.Ports;
using Mise.Modules.Reservations.Domain;
using Mise.SharedKernel;
using Mise.SharedKernel.Infrastructure;
using Moq;

namespace Mise.UnitTests.Reservations;

public class CancelReservationCommandHandlerTests
{
    private readonly Mock<IReservationsData> _reservationsData = new();
    private readonly Mock<IAuditWriter> _auditWriter = new();
    private readonly FakeTimeProvider _timeProvider = new(DateTimeOffset.Parse("2026-09-15T18:00:00+02:00"));
    private readonly CancelReservationCommandHandler _sut;

    public CancelReservationCommandHandlerTests()
    {
        _sut = new CancelReservationCommandHandler(
            _reservationsData.Object, _auditWriter.Object, _timeProvider, NullLogger<CancelReservationCommandHandler>.Instance);
    }

    private Reservation ExistingReservation() =>
        Reservation.Create(
            Guid.NewGuid(), "Jane Doe", "+32 470 00 00 00", null, 4,
            DateTimeOffset.Parse("2026-09-20T19:00:00+02:00"), 90, null, null, Guid.NewGuid(), _timeProvider.GetUtcNow());

    private static CancelReservationCommand CommandFor(Reservation reservation, uint expectedVersion = 1) =>
        new(Guid.NewGuid(), reservation.Id, expectedVersion, Guid.NewGuid());

    [Fact]
    public async Task HandleAsync_ReservationDoesNotExist_ReturnsNull()
    {
        var reservation = ExistingReservation();
        var command = CommandFor(reservation);
        _reservationsData.Setup(d => d.GetReservationByIdAsync(reservation.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ReservationWithVersion?)null);

        var result = await _sut.HandleAsync(command, CancellationToken.None);

        result.Should().BeNull();
        _auditWriter.Verify(a => a.WriteAsync(It.IsAny<AuditLogEntry>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_ValidCommand_CancelsViaGatewayAndWritesOneAuditEntry()
    {
        var reservation = ExistingReservation();
        var command = CommandFor(reservation);
        _reservationsData.Setup(d => d.GetReservationByIdAsync(reservation.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReservationWithVersion(reservation, 1));
        _reservationsData
            .Setup(d => d.CancelReservationAsync(It.IsAny<Reservation>(), command.ExpectedVersion, command.OperationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReservationSaveResult(ReservationSaveOutcome.Saved, reservation, 2, WasAlreadyProcessed: false));

        var result = await _sut.HandleAsync(command, CancellationToken.None);

        result.Should().NotBeNull();
        reservation.Status.Should().Be(ReservationStatus.Cancelled,
            because: "the handler must call Cancel on the loaded aggregate before persisting.");
        _auditWriter.Verify(
            a => a.WriteAsync(It.Is<AuditLogEntry>(e => e.EntityType == "Reservation" && e.Action == "Cancelled"), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task HandleAsync_StaleExpectedVersion_ThrowsConcurrencyConflictExceptionAndSkipsTheAuditWrite()
    {
        var reservation = ExistingReservation();
        var command = CommandFor(reservation);
        _reservationsData.Setup(d => d.GetReservationByIdAsync(reservation.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReservationWithVersion(reservation, 1));
        _reservationsData
            .Setup(d => d.CancelReservationAsync(It.IsAny<Reservation>(), command.ExpectedVersion, command.OperationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReservationSaveResult(ReservationSaveOutcome.VersionMismatch, reservation, 5, WasAlreadyProcessed: false));

        var act = () => _sut.HandleAsync(command, CancellationToken.None);

        var exception = await act.Should().ThrowAsync<ConcurrencyConflictException>();
        exception.Which.CurrentVersion.Should().Be(5u);
        _auditWriter.Verify(a => a.WriteAsync(It.IsAny<AuditLogEntry>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_OperationIdAlreadyProcessed_SkipsTheAuditWrite()
    {
        var reservation = ExistingReservation();
        var command = CommandFor(reservation);
        _reservationsData.Setup(d => d.GetReservationByIdAsync(reservation.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReservationWithVersion(reservation, 1));
        _reservationsData
            .Setup(d => d.CancelReservationAsync(It.IsAny<Reservation>(), command.ExpectedVersion, command.OperationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReservationSaveResult(ReservationSaveOutcome.Saved, reservation, 1, WasAlreadyProcessed: true));

        await _sut.HandleAsync(command, CancellationToken.None);

        _auditWriter.Verify(a => a.WriteAsync(It.IsAny<AuditLogEntry>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
