using FluentValidation;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Mise.Modules.Reservations.Application.Ports;
using Mise.Modules.Reservations.Application.UpdateReservation;
using Mise.Modules.Reservations.Domain;
using Mise.Modules.Tables.Contracts;
using Mise.SharedKernel;
using Mise.SharedKernel.Infrastructure;
using Moq;

namespace Mise.UnitTests.Reservations;

public class UpdateReservationCommandHandlerTests
{
    private readonly Mock<IReservationsData> _reservationsData = new();
    private readonly Mock<ITableAvailabilityLookup> _tableAvailabilityLookup = new();
    private readonly Mock<IAuditWriter> _auditWriter = new();
    private readonly FakeTimeProvider _timeProvider = new(DateTimeOffset.Parse("2026-09-15T18:00:00+02:00"));
    private readonly UpdateReservationCommandHandler _sut;

    public UpdateReservationCommandHandlerTests()
    {
        _sut = new UpdateReservationCommandHandler(
            _reservationsData.Object, _tableAvailabilityLookup.Object, _auditWriter.Object,
            new UpdateReservationCommandValidator(), _timeProvider, NullLogger<UpdateReservationCommandHandler>.Instance);
    }

    private Reservation ExistingReservation() =>
        Reservation.Create(
            Guid.NewGuid(), "Jane Doe", "+32 470 00 00 00", null, 4,
            DateTimeOffset.Parse("2026-09-20T19:00:00+02:00"), 90, null, null, Guid.NewGuid(), _timeProvider.GetUtcNow());

    private static UpdateReservationCommand ValidCommand(Reservation reservation, uint expectedVersion = 1, Guid? tableId = null) =>
        new(Guid.NewGuid(), reservation.Id, expectedVersion, "John Smith", "+32 470 11 11 11", null, 6,
            reservation.ReservationDateTime, 120, tableId, "VIP", Guid.NewGuid());

    [Fact]
    public async Task HandleAsync_InvalidCommand_ThrowsValidationExceptionAndNeverTouchesTheGateway()
    {
        var reservation = ExistingReservation();
        var command = ValidCommand(reservation) with { CustomerPhone = "" };

        var act = () => _sut.HandleAsync(command, CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>();
        _reservationsData.Verify(d => d.GetReservationByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_ReservationDoesNotExist_ReturnsNull()
    {
        var reservation = ExistingReservation();
        var command = ValidCommand(reservation);
        _reservationsData.Setup(d => d.GetReservationByIdAsync(reservation.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ReservationWithVersion?)null);

        var result = await _sut.HandleAsync(command, CancellationToken.None);

        result.Should().BeNull();
        _auditWriter.Verify(a => a.WriteAsync(It.IsAny<AuditLogEntry>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_ReservationAlreadyCancelled_ThrowsDomainRuleViolationException()
    {
        var reservation = ExistingReservation();
        reservation.Cancel(_timeProvider.GetUtcNow());
        var command = ValidCommand(reservation);
        _reservationsData.Setup(d => d.GetReservationByIdAsync(reservation.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReservationWithVersion(reservation, 1));

        var act = () => _sut.HandleAsync(command, CancellationToken.None);

        await act.Should().ThrowAsync<DomainRuleViolationException>();
        _reservationsData.Verify(
            d => d.UpdateReservationAsync(It.IsAny<Reservation>(), It.IsAny<uint>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task HandleAsync_ValidCommand_UpdatesViaGatewayAndWritesOneAuditEntry()
    {
        var reservation = ExistingReservation();
        var command = ValidCommand(reservation);
        _reservationsData.Setup(d => d.GetReservationByIdAsync(reservation.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReservationWithVersion(reservation, 1));
        _reservationsData
            .Setup(d => d.UpdateReservationAsync(It.IsAny<Reservation>(), command.ExpectedVersion, command.OperationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReservationSaveResult(ReservationSaveOutcome.Saved, reservation, 2, WasAlreadyProcessed: false));

        var result = await _sut.HandleAsync(command, CancellationToken.None);

        result.Should().NotBeNull();
        result!.Version.Should().Be(2u);
        reservation.CustomerName.Should().Be("John Smith", because: "the handler must call UpdateDetails on the loaded aggregate before persisting.");
        _auditWriter.Verify(
            a => a.WriteAsync(It.Is<AuditLogEntry>(e => e.EntityType == "Reservation" && e.Action == "Updated"), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task HandleAsync_TableIdExceedsCapacity_ThrowsValidationExceptionAndNeverTouchesTheGateway()
    {
        var reservation = ExistingReservation();
        var tableId = Guid.NewGuid();
        var command = ValidCommand(reservation, tableId: tableId) with { PartySize = 20 };
        _reservationsData.Setup(d => d.GetReservationByIdAsync(reservation.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReservationWithVersion(reservation, 1));
        _tableAvailabilityLookup.Setup(l => l.GetCapacityInfoAsync(tableId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TableCapacityInfo(tableId, IsActive: true, 2, 4));

        var act = () => _sut.HandleAsync(command, CancellationToken.None);

        var exception = await act.Should().ThrowAsync<ValidationException>();
        exception.Which.Errors.Should().ContainSingle(e => e.PropertyName == "PartySize");
        _reservationsData.Verify(
            d => d.UpdateReservationAsync(It.IsAny<Reservation>(), It.IsAny<uint>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task HandleAsync_StaleExpectedVersion_ThrowsConcurrencyConflictExceptionAndSkipsTheAuditWrite()
    {
        var reservation = ExistingReservation();
        var command = ValidCommand(reservation);
        _reservationsData.Setup(d => d.GetReservationByIdAsync(reservation.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReservationWithVersion(reservation, 1));
        _reservationsData
            .Setup(d => d.UpdateReservationAsync(It.IsAny<Reservation>(), command.ExpectedVersion, command.OperationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReservationSaveResult(ReservationSaveOutcome.VersionMismatch, reservation, 5, WasAlreadyProcessed: false));

        var act = () => _sut.HandleAsync(command, CancellationToken.None);

        var exception = await act.Should().ThrowAsync<ConcurrencyConflictException>();
        exception.Which.CurrentVersion.Should().Be(5u);
        _auditWriter.Verify(a => a.WriteAsync(It.IsAny<AuditLogEntry>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_GatewayReportsTableOverlap_ThrowsReservationOverlapException()
    {
        var reservation = ExistingReservation();
        var tableId = Guid.NewGuid();
        var command = ValidCommand(reservation, tableId: tableId);
        _reservationsData.Setup(d => d.GetReservationByIdAsync(reservation.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReservationWithVersion(reservation, 1));
        _tableAvailabilityLookup.Setup(l => l.GetCapacityInfoAsync(tableId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TableCapacityInfo(tableId, IsActive: true, 2, 10));
        _reservationsData
            .Setup(d => d.UpdateReservationAsync(It.IsAny<Reservation>(), command.ExpectedVersion, command.OperationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReservationSaveResult(ReservationSaveOutcome.TableOverlap, reservation, 1, WasAlreadyProcessed: false));

        var act = () => _sut.HandleAsync(command, CancellationToken.None);

        var exception = await act.Should().ThrowAsync<ReservationOverlapException>();
        exception.Which.TableId.Should().Be(tableId);
    }

    [Fact]
    public async Task HandleAsync_OperationIdAlreadyProcessed_SkipsTheAuditWrite()
    {
        var reservation = ExistingReservation();
        var command = ValidCommand(reservation);
        _reservationsData.Setup(d => d.GetReservationByIdAsync(reservation.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReservationWithVersion(reservation, 1));
        _reservationsData
            .Setup(d => d.UpdateReservationAsync(It.IsAny<Reservation>(), command.ExpectedVersion, command.OperationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReservationSaveResult(ReservationSaveOutcome.Saved, reservation, 1, WasAlreadyProcessed: true));

        await _sut.HandleAsync(command, CancellationToken.None);

        _auditWriter.Verify(a => a.WriteAsync(It.IsAny<AuditLogEntry>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
