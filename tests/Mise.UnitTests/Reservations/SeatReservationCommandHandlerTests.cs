using FluentValidation;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Mise.Modules.Reservations.Application.Ports;
using Mise.Modules.Reservations.Application.SeatReservation;
using Mise.Modules.Reservations.Contracts;
using Mise.Modules.Reservations.Domain;
using Mise.Modules.Tables.Contracts;
using Mise.SharedKernel;
using Mise.SharedKernel.Infrastructure;
using Moq;

namespace Mise.UnitTests.Reservations;

public class SeatReservationCommandHandlerTests
{
    private readonly Mock<IReservationsData> _reservationsData = new();
    private readonly Mock<ITableAvailabilityLookup> _tableAvailabilityLookup = new();
    private readonly Mock<IAuditWriter> _auditWriter = new();
    private readonly Mock<IDomainEventPublisher> _domainEventPublisher = new();
    private readonly FakeTimeProvider _timeProvider = new(DateTimeOffset.Parse("2026-09-15T18:00:00+02:00"));
    private readonly SeatReservationCommandHandler _sut;

    public SeatReservationCommandHandlerTests()
    {
        _sut = new SeatReservationCommandHandler(
            _reservationsData.Object, _tableAvailabilityLookup.Object, _auditWriter.Object, _domainEventPublisher.Object,
            new SeatReservationCommandValidator(), _timeProvider, NullLogger<SeatReservationCommandHandler>.Instance);
    }

    private Reservation ExistingReservation() =>
        Reservation.Create(
            Guid.NewGuid(), "Jane Doe", "+32 470 00 00 00", null, 4,
            DateTimeOffset.Parse("2026-09-20T19:00:00+02:00"), 90, null, null, Guid.NewGuid(), _timeProvider.GetUtcNow());

    private static SeatReservationCommand ValidCommand(Reservation reservation, Guid tableId, uint expectedVersion = 1) =>
        new(Guid.NewGuid(), reservation.Id, expectedVersion, tableId, Guid.NewGuid());

    private void AllowAnyTable(Guid tableId, int min = 2, int max = 4) =>
        _tableAvailabilityLookup.Setup(l => l.GetCapacityInfoAsync(tableId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TableCapacityInfo(tableId, IsActive: true, min, max));

    [Fact]
    public async Task HandleAsync_TableIdEmpty_ThrowsValidationExceptionAndNeverTouchesTheGateway()
    {
        var reservation = ExistingReservation();
        var command = ValidCommand(reservation, Guid.Empty);

        var act = () => _sut.HandleAsync(command, CancellationToken.None);

        var exception = await act.Should().ThrowAsync<ValidationException>();
        exception.Which.Errors.Should().ContainSingle(e => e.PropertyName == "TableId");
        _reservationsData.Verify(d => d.GetReservationByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_ReservationDoesNotExist_ReturnsNull()
    {
        var reservation = ExistingReservation();
        var tableId = Guid.NewGuid();
        var command = ValidCommand(reservation, tableId);
        _reservationsData.Setup(d => d.GetReservationByIdAsync(reservation.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ReservationWithVersion?)null);

        var result = await _sut.HandleAsync(command, CancellationToken.None);

        result.Should().BeNull();
        _auditWriter.Verify(a => a.WriteAsync(It.IsAny<AuditLogEntry>(), It.IsAny<CancellationToken>()), Times.Never);
        _domainEventPublisher.Verify(p => p.PublishAsync(It.IsAny<ReservationSeated>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_ReservationAlreadyCancelled_ThrowsDomainRuleViolationException()
    {
        var reservation = ExistingReservation();
        reservation.Cancel(_timeProvider.GetUtcNow());
        var tableId = Guid.NewGuid();
        var command = ValidCommand(reservation, tableId);
        _reservationsData.Setup(d => d.GetReservationByIdAsync(reservation.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReservationWithVersion(reservation, 1));
        AllowAnyTable(tableId);

        var act = () => _sut.HandleAsync(command, CancellationToken.None);

        await act.Should().ThrowAsync<DomainRuleViolationException>();
        _reservationsData.Verify(
            d => d.UpdateReservationAsync(It.IsAny<Reservation>(), It.IsAny<uint>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task HandleAsync_TableIdExceedsCapacity_ThrowsValidationExceptionAndNeverTouchesTheGateway()
    {
        var reservation = ExistingReservation();
        var tableId = Guid.NewGuid();
        var command = ValidCommand(reservation, tableId);
        _reservationsData.Setup(d => d.GetReservationByIdAsync(reservation.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReservationWithVersion(reservation, 1));
        AllowAnyTable(tableId, min: 6, max: 10);

        var act = () => _sut.HandleAsync(command, CancellationToken.None);

        var exception = await act.Should().ThrowAsync<ValidationException>();
        exception.Which.Errors.Should().ContainSingle(e => e.PropertyName == "PartySize");
        _reservationsData.Verify(
            d => d.UpdateReservationAsync(It.IsAny<Reservation>(), It.IsAny<uint>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task HandleAsync_ValidCommand_SeatsViaGatewayAndWritesOneAuditEntryAndPublishesReservationSeated()
    {
        var reservation = ExistingReservation();
        var tableId = Guid.NewGuid();
        var command = ValidCommand(reservation, tableId);
        _reservationsData.Setup(d => d.GetReservationByIdAsync(reservation.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReservationWithVersion(reservation, 1));
        AllowAnyTable(tableId);
        _reservationsData
            .Setup(d => d.UpdateReservationAsync(It.IsAny<Reservation>(), command.ExpectedVersion, command.OperationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReservationSaveResult(ReservationSaveOutcome.Saved, reservation, 2, WasAlreadyProcessed: false));

        var result = await _sut.HandleAsync(command, CancellationToken.None);

        result.Should().NotBeNull();
        reservation.Status.Should().Be(ReservationStatus.Seated, because: "the handler must call MarkSeated on the loaded aggregate before persisting.");
        reservation.TableId.Should().Be(tableId);
        _auditWriter.Verify(
            a => a.WriteAsync(It.Is<AuditLogEntry>(e => e.EntityType == "Reservation" && e.Action == "Seated"), It.IsAny<CancellationToken>()),
            Times.Once);
        _domainEventPublisher.Verify(
            p => p.PublishAsync(
                It.Is<ReservationSeated>(e => e.ReservationId == reservation.Id && e.TableId == tableId),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task HandleAsync_StaleExpectedVersion_ThrowsConcurrencyConflictExceptionAndSkipsTheAuditWriteAndEvent()
    {
        var reservation = ExistingReservation();
        var tableId = Guid.NewGuid();
        var command = ValidCommand(reservation, tableId);
        _reservationsData.Setup(d => d.GetReservationByIdAsync(reservation.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReservationWithVersion(reservation, 1));
        AllowAnyTable(tableId);
        _reservationsData
            .Setup(d => d.UpdateReservationAsync(It.IsAny<Reservation>(), command.ExpectedVersion, command.OperationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReservationSaveResult(ReservationSaveOutcome.VersionMismatch, reservation, 5, WasAlreadyProcessed: false));

        var act = () => _sut.HandleAsync(command, CancellationToken.None);

        var exception = await act.Should().ThrowAsync<ConcurrencyConflictException>();
        exception.Which.CurrentVersion.Should().Be(5u);
        _auditWriter.Verify(a => a.WriteAsync(It.IsAny<AuditLogEntry>(), It.IsAny<CancellationToken>()), Times.Never);
        _domainEventPublisher.Verify(p => p.PublishAsync(It.IsAny<ReservationSeated>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_GatewayReportsTableOverlap_ThrowsReservationOverlapExceptionAndSkipsTheEvent()
    {
        var reservation = ExistingReservation();
        var tableId = Guid.NewGuid();
        var command = ValidCommand(reservation, tableId);
        _reservationsData.Setup(d => d.GetReservationByIdAsync(reservation.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReservationWithVersion(reservation, 1));
        AllowAnyTable(tableId);
        _reservationsData
            .Setup(d => d.UpdateReservationAsync(It.IsAny<Reservation>(), command.ExpectedVersion, command.OperationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReservationSaveResult(ReservationSaveOutcome.TableOverlap, reservation, 1, WasAlreadyProcessed: false));

        var act = () => _sut.HandleAsync(command, CancellationToken.None);

        var exception = await act.Should().ThrowAsync<ReservationOverlapException>();
        exception.Which.TableId.Should().Be(tableId);
        _domainEventPublisher.Verify(p => p.PublishAsync(It.IsAny<ReservationSeated>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_OperationIdAlreadyProcessed_SkipsTheAuditWriteButStillPublishesReservationSeated()
    {
        var reservation = ExistingReservation();
        var tableId = Guid.NewGuid();
        var command = ValidCommand(reservation, tableId);
        _reservationsData.Setup(d => d.GetReservationByIdAsync(reservation.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReservationWithVersion(reservation, 1));
        AllowAnyTable(tableId);
        _reservationsData
            .Setup(d => d.UpdateReservationAsync(It.IsAny<Reservation>(), command.ExpectedVersion, command.OperationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReservationSaveResult(ReservationSaveOutcome.Saved, reservation, 1, WasAlreadyProcessed: true));

        await _sut.HandleAsync(command, CancellationToken.None);

        _auditWriter.Verify(a => a.WriteAsync(It.IsAny<AuditLogEntry>(), It.IsAny<CancellationToken>()), Times.Never);
        _domainEventPublisher.Verify(
            p => p.PublishAsync(It.IsAny<ReservationSeated>(), It.IsAny<CancellationToken>()), Times.Once,
            "unlike the audit write, the event is redispatched on replay so a failed cross-module side effect from the first attempt can self-heal.");
    }
}
