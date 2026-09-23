using FluentValidation;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Mise.Modules.Reservations.Application;
using Mise.Modules.Reservations.Application.CreateReservation;
using Mise.Modules.Reservations.Application.Ports;
using Mise.Modules.Reservations.Domain;
using Mise.Modules.Tables.Contracts;
using Mise.SharedKernel;
using Mise.SharedKernel.Infrastructure;
using Moq;

namespace Mise.UnitTests.Reservations;

public class CreateReservationCommandHandlerTests
{
    private readonly Mock<IReservationsData> _reservationsData = new();
    private readonly Mock<ITableAvailabilityLookup> _tableAvailabilityLookup = new();
    private readonly Mock<IAuditWriter> _auditWriter = new();
    private readonly Mock<IRealtimeNotifier> _realtimeNotifier = new();
    private readonly FakeTimeProvider _timeProvider = new(DateTimeOffset.Parse("2026-09-15T18:00:00+02:00"));
    private readonly CreateReservationCommandHandler _sut;

    public CreateReservationCommandHandlerTests()
    {
        _sut = new CreateReservationCommandHandler(
            _reservationsData.Object,
            _tableAvailabilityLookup.Object,
            _auditWriter.Object,
            _realtimeNotifier.Object,
            new CreateReservationCommandValidator(),
            Options.Create(new ReservationDefaultsOptions()),
            _timeProvider,
            NullLogger<CreateReservationCommandHandler>.Instance);
    }

    private static readonly Guid PerformingStaffId = Guid.NewGuid();

    private static CreateReservationCommand ValidCommand(
        int partySize = 4, int? durationMinutes = null, Guid? tableId = null) =>
        new(Guid.NewGuid(), "Jane Doe", "+32 470 00 00 00", null, partySize,
            DateTimeOffset.Parse("2026-09-20T19:00:00+02:00"), durationMinutes, tableId, null, PerformingStaffId);

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
        _auditWriter.Verify(a => a.Stage(It.IsAny<AuditLogEntry>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_ValidCommand_PersistsViaGatewayAndWritesOneAuditEntry()
    {
        var command = ValidCommand();
        var reservationId = Guid.NewGuid();
        _reservationsData
            .Setup(d => d.CreateReservationAsync(It.IsAny<Reservation>(), command.OperationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Reservation r, Guid _, CancellationToken _) =>
                new ReservationSaveResult(ReservationSaveOutcome.Saved, r, 1, WasAlreadyProcessed: false));

        var result = await _sut.HandleAsync(command, CancellationToken.None);

        result.Reservation.Id.Should().NotBeEmpty();
        _reservationsData.Verify(
            d => d.CreateReservationAsync(
                It.Is<Reservation>(r =>
                    r.CustomerName == command.CustomerName && r.CustomerPhone == command.CustomerPhone
                    && r.PartySize == command.PartySize && r.DurationMinutes == 90),
                command.OperationId,
                It.IsAny<CancellationToken>()),
            Times.Once,
            "a null DurationMinutes must fall back to ReservationDefaultsOptions.DefaultDurationMinutes (90).");
        _auditWriter.Verify(
            a => a.Stage(
                It.Is<AuditLogEntry>(e =>
                    e.EntityId == result.Reservation.Id
                    && e.Action == "Created"
                    && e.PerformedByStaffId == command.PerformedByStaffId
                    && e.OccurredAtUtc == _timeProvider.GetUtcNow())),
            Times.Once,
            "the audit entry's timestamp must come from the injected TimeProvider, never the wall clock.");
        _realtimeNotifier.Verify(
            n => n.NotifyReservationCreatedAsync(
                It.Is<ReservationChangedNotification>(p => p.ReservationId == result.Reservation.Id), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task HandleAsync_DurationMinutesProvided_OverridesTheConfiguredDefault()
    {
        var command = ValidCommand(durationMinutes: 45);
        _reservationsData
            .Setup(d => d.CreateReservationAsync(It.IsAny<Reservation>(), command.OperationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Reservation r, Guid _, CancellationToken _) =>
                new ReservationSaveResult(ReservationSaveOutcome.Saved, r, 1, WasAlreadyProcessed: false));

        await _sut.HandleAsync(command, CancellationToken.None);

        _reservationsData.Verify(
            d => d.CreateReservationAsync(It.Is<Reservation>(r => r.DurationMinutes == 45), command.OperationId, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task HandleAsync_OperationIdAlreadyProcessed_StagesAuditEntryRegardlessButGatewayNeverFlushesIt()
    {
        var command = ValidCommand();
        var existing = Reservation.Create(
            Guid.NewGuid(), "Jane Doe", "+32 470 00 00 00", null, 4, command.ReservationDateTime, 90, null, null,
            PerformingStaffId, _timeProvider.GetUtcNow());
        _reservationsData
            .Setup(d => d.CreateReservationAsync(It.IsAny<Reservation>(), command.OperationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReservationSaveResult(ReservationSaveOutcome.Saved, existing, 1, WasAlreadyProcessed: true));

        var result = await _sut.HandleAsync(command, CancellationToken.None);

        result.Reservation.Id.Should().Be(existing.Id,
            because: "a replayed OperationId returns the reservation it originally created (docs/plan.md's OperationId replay contract).");
        _auditWriter.Verify(
            a => a.Stage(It.IsAny<AuditLogEntry>()),
            Times.Once,
            "Stage is called unconditionally before the gateway call (Phase 9/ADR-009's stage-before-mutate design) — " +
            "the actual 'no second audit row' guarantee is structural, not something this mocked-gateway unit test can " +
            "observe: the real gateway's own SaveChangesAsync is never reached on a replay, so the staged-but-unflushed " +
            "entry is discarded with the DbContext (proven at the integration tier instead).");
        _realtimeNotifier.Verify(
            n => n.NotifyReservationCreatedAsync(It.IsAny<ReservationChangedNotification>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "nothing changed on a replay, so there is nothing new to broadcast (unlike the ADR-007 cross-module dispatch).");
    }

    [Fact]
    public async Task HandleAsync_TableIdDoesNotExist_ThrowsValidationExceptionAndNeverTouchesTheGateway()
    {
        var tableId = Guid.NewGuid();
        var command = ValidCommand(tableId: tableId);
        _tableAvailabilityLookup
            .Setup(l => l.GetCapacityInfoAsync(tableId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((TableCapacityInfo?)null);

        var act = () => _sut.HandleAsync(command, CancellationToken.None);

        var exception = await act.Should().ThrowAsync<ValidationException>();
        exception.Which.Errors.Should().ContainSingle(e => e.PropertyName == "TableId")
            .Which.ErrorMessage.Should().Be("TableId does not refer to an existing table.");
        _reservationsData.Verify(
            d => d.CreateReservationAsync(It.IsAny<Reservation>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_TableIdInactive_ThrowsValidationException()
    {
        var tableId = Guid.NewGuid();
        var command = ValidCommand(tableId: tableId);
        _tableAvailabilityLookup
            .Setup(l => l.GetCapacityInfoAsync(tableId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TableCapacityInfo(tableId, IsActive: false, 2, 4));

        var act = () => _sut.HandleAsync(command, CancellationToken.None);

        var exception = await act.Should().ThrowAsync<ValidationException>();
        exception.Which.Errors.Should().ContainSingle(e => e.PropertyName == "TableId")
            .Which.ErrorMessage.Should().Be("TableId refers to an inactive table.");
    }

    [Fact]
    public async Task HandleAsync_PartySizeExceedsTableCapacity_ThrowsValidationExceptionOnPartySize()
    {
        var tableId = Guid.NewGuid();
        var command = ValidCommand(partySize: 8, tableId: tableId);
        _tableAvailabilityLookup
            .Setup(l => l.GetCapacityInfoAsync(tableId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TableCapacityInfo(tableId, IsActive: true, CombinedMinCapacity: 2, CombinedMaxCapacity: 4));

        var act = () => _sut.HandleAsync(command, CancellationToken.None);

        var exception = await act.Should().ThrowAsync<ValidationException>(because: "BR-07: party size must fit the assigned table's capacity.");
        exception.Which.Errors.Should().ContainSingle(e => e.PropertyName == "PartySize")
            .Which.ErrorMessage.Should().Be("PartySize does not fit the assigned table's capacity.");
        _reservationsData.Verify(
            d => d.CreateReservationAsync(It.IsAny<Reservation>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_PartySizeWithinCombinedGroupCapacity_Succeeds()
    {
        var tableId = Guid.NewGuid();
        var command = ValidCommand(partySize: 8, tableId: tableId);
        _tableAvailabilityLookup
            .Setup(l => l.GetCapacityInfoAsync(tableId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TableCapacityInfo(tableId, IsActive: true, CombinedMinCapacity: 4, CombinedMaxCapacity: 10));
        _reservationsData
            .Setup(d => d.CreateReservationAsync(It.IsAny<Reservation>(), command.OperationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Reservation r, Guid _, CancellationToken _) =>
                new ReservationSaveResult(ReservationSaveOutcome.Saved, r, 1, WasAlreadyProcessed: false));

        var result = await _sut.HandleAsync(command, CancellationToken.None);

        result.Outcome.Should().Be(ReservationSaveOutcome.Saved,
            because: "BR-07's 'or an explicitly combinable set of tables' clause is satisfied by the combined capacity.");
    }

    [Fact]
    public async Task HandleAsync_GatewayReportsTableOverlap_ThrowsReservationOverlapException()
    {
        var tableId = Guid.NewGuid();
        var command = ValidCommand(tableId: tableId);
        _tableAvailabilityLookup
            .Setup(l => l.GetCapacityInfoAsync(tableId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TableCapacityInfo(tableId, IsActive: true, 2, 4));
        _reservationsData
            .Setup(d => d.CreateReservationAsync(It.IsAny<Reservation>(), command.OperationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Reservation r, Guid _, CancellationToken _) =>
                new ReservationSaveResult(ReservationSaveOutcome.TableOverlap, r, 0, WasAlreadyProcessed: false));

        var act = () => _sut.HandleAsync(command, CancellationToken.None);

        var exception = await act.Should().ThrowAsync<ReservationOverlapException>(
            because: "docs/plan.md correction #1 — BR-01 surfaces as a clean 409 via this exception, never a raw constraint error.");
        exception.Which.TableId.Should().Be(tableId);
        _auditWriter.Verify(
            a => a.Stage(It.IsAny<AuditLogEntry>()),
            Times.Once,
            "Stage is called before the gateway call, so it still fires even though the gateway's own overlap check " +
            "(reported via its return value here) rejects the mutation — nothing is ever flushed for this path.");
        _realtimeNotifier.Verify(
            n => n.NotifyReservationCreatedAsync(It.IsAny<ReservationChangedNotification>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
