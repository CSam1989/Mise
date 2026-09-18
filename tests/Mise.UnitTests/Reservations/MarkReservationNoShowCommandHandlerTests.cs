using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Mise.Modules.Reservations.Application.MarkReservationNoShow;
using Mise.Modules.Reservations.Application.Ports;
using Mise.Modules.Reservations.Contracts;
using Mise.Modules.Reservations.Domain;
using Mise.SharedKernel;
using Mise.SharedKernel.Infrastructure;
using Moq;

namespace Mise.UnitTests.Reservations;

public class MarkReservationNoShowCommandHandlerTests
{
    private readonly Mock<IReservationsData> _reservationsData = new();
    private readonly Mock<IAuditWriter> _auditWriter = new();
    private readonly Mock<IDomainEventPublisher> _domainEventPublisher = new();
    private readonly FakeTimeProvider _timeProvider = new(DateTimeOffset.Parse("2026-09-15T18:00:00+02:00"));
    private readonly MarkReservationNoShowCommandHandler _sut;

    public MarkReservationNoShowCommandHandlerTests()
    {
        _sut = new MarkReservationNoShowCommandHandler(
            _reservationsData.Object, _auditWriter.Object, _domainEventPublisher.Object, _timeProvider,
            NullLogger<MarkReservationNoShowCommandHandler>.Instance);
    }

    private Reservation ExistingReservation(Guid? tableId = null) =>
        Reservation.Create(
            Guid.NewGuid(), "Jane Doe", "+32 470 00 00 00", null, 4,
            DateTimeOffset.Parse("2026-09-20T19:00:00+02:00"), 90, tableId, null, Guid.NewGuid(), _timeProvider.GetUtcNow());

    private static MarkReservationNoShowCommand CommandFor(Reservation reservation, uint expectedVersion = 1) =>
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
    public async Task HandleAsync_ValidCommand_MarksNoShowViaGatewayAndWritesOneAuditEntry()
    {
        var reservation = ExistingReservation();
        var command = CommandFor(reservation);
        _reservationsData.Setup(d => d.GetReservationByIdAsync(reservation.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReservationWithVersion(reservation, 1));
        _reservationsData
            .Setup(d => d.MarkNoShowAsync(It.IsAny<Reservation>(), command.ExpectedVersion, command.OperationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReservationSaveResult(ReservationSaveOutcome.Saved, reservation, 2, WasAlreadyProcessed: false));

        var result = await _sut.HandleAsync(command, CancellationToken.None);

        result.Should().NotBeNull();
        reservation.Status.Should().Be(ReservationStatus.NoShow, because: "the handler must call MarkNoShow on the loaded aggregate before persisting.");
        _auditWriter.Verify(
            a => a.WriteAsync(It.Is<AuditLogEntry>(e => e.EntityType == "Reservation" && e.Action == "NoShow"), It.IsAny<CancellationToken>()),
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
            .Setup(d => d.MarkNoShowAsync(It.IsAny<Reservation>(), command.ExpectedVersion, command.OperationId, It.IsAny<CancellationToken>()))
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
            .Setup(d => d.MarkNoShowAsync(It.IsAny<Reservation>(), command.ExpectedVersion, command.OperationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReservationSaveResult(ReservationSaveOutcome.Saved, reservation, 1, WasAlreadyProcessed: true));

        await _sut.HandleAsync(command, CancellationToken.None);

        _auditWriter.Verify(a => a.WriteAsync(It.IsAny<AuditLogEntry>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_ReservationHasNoTableAssigned_DoesNotPublishReservationTableVacated()
    {
        var reservation = ExistingReservation(tableId: null);
        var command = CommandFor(reservation);
        _reservationsData.Setup(d => d.GetReservationByIdAsync(reservation.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReservationWithVersion(reservation, 1));
        _reservationsData
            .Setup(d => d.MarkNoShowAsync(It.IsAny<Reservation>(), command.ExpectedVersion, command.OperationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReservationSaveResult(ReservationSaveOutcome.Saved, reservation, 2, WasAlreadyProcessed: false));

        await _sut.HandleAsync(command, CancellationToken.None);

        _domainEventPublisher.Verify(p => p.PublishAsync(It.IsAny<ReservationTableVacated>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_ReservationHasTableAssigned_PublishesReservationTableVacated()
    {
        var tableId = Guid.NewGuid();
        var reservation = ExistingReservation(tableId);
        var command = CommandFor(reservation);
        _reservationsData.Setup(d => d.GetReservationByIdAsync(reservation.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReservationWithVersion(reservation, 1));
        _reservationsData
            .Setup(d => d.MarkNoShowAsync(It.IsAny<Reservation>(), command.ExpectedVersion, command.OperationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReservationSaveResult(ReservationSaveOutcome.Saved, reservation, 2, WasAlreadyProcessed: false));

        await _sut.HandleAsync(command, CancellationToken.None);

        _domainEventPublisher.Verify(
            p => p.PublishAsync(
                It.Is<ReservationTableVacated>(e => e.ReservationId == reservation.Id && e.TableId == tableId),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
