using Mise.Modules.Reservations.Domain;
using Mise.SharedKernel;

namespace Mise.UnitTests.Reservations;

public class ReservationTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-01T12:00:00Z");

    private static Reservation CreateValid(int partySize = 4, int durationMinutes = 90, Guid? tableId = null) =>
        Reservation.Create(
            Guid.NewGuid(), "Jane Doe", "+32 470 00 00 00", "jane@example.com", partySize,
            Now.AddDays(1), durationMinutes, tableId, "No nuts", Guid.NewGuid(), Now);

    [Fact]
    public void Create_PartySizeZero_ThrowsArgumentOutOfRangeException()
    {
        var act = () => Reservation.Create(
            Guid.NewGuid(), "Jane Doe", "+32 470 00 00 00", null, 0, Now.AddDays(1), 90, null, null, Guid.NewGuid(), Now);

        act.Should().Throw<ArgumentOutOfRangeException>(because: "a non-positive party size is invalid.");
    }

    [Fact]
    public void Create_DurationMinutesZero_ThrowsArgumentOutOfRangeException()
    {
        var act = () => Reservation.Create(
            Guid.NewGuid(), "Jane Doe", "+32 470 00 00 00", null, 4, Now.AddDays(1), 0, null, null, Guid.NewGuid(), Now);

        act.Should().Throw<ArgumentOutOfRangeException>(because: "a non-positive duration is invalid.");
    }

    [Fact]
    public void Create_CustomerNameEmpty_ThrowsArgumentException()
    {
        var act = () => Reservation.Create(
            Guid.NewGuid(), "   ", "+32 470 00 00 00", null, 4, Now.AddDays(1), 90, null, null, Guid.NewGuid(), Now);

        act.Should().Throw<ArgumentException>(because: "Guard.Against.NullOrWhiteSpace backstops the customer name.");
    }

    [Fact]
    public void Create_CustomerPhoneEmpty_ThrowsArgumentException()
    {
        var act = () => Reservation.Create(
            Guid.NewGuid(), "Jane Doe", "   ", null, 4, Now.AddDays(1), 90, null, null, Guid.NewGuid(), Now);

        act.Should().Throw<ArgumentException>(because: "FR-01 names phone as a required capture field, same Domain-boundary guard as CustomerName.");
    }

    [Fact]
    public void Create_ValidInput_StartsConfirmedWithMatchingTimestamps()
    {
        var reservation = CreateValid();

        reservation.Status.Should().Be(ReservationStatus.Confirmed, because: "AQ-01 (resolved): every new reservation starts Confirmed.");
        reservation.CreatedAtUtc.Should().Be(Now);
        reservation.UpdatedAtUtc.Should().Be(Now, because: "CreatedAt and UpdatedAt must match on creation.");
    }

    [Fact]
    public void UpdateDetails_StatusConfirmed_UpdatesFieldsAndBumpsUpdatedAt()
    {
        var reservation = CreateValid();
        var later = Now.AddHours(1);

        reservation.UpdateDetails(
            "John Smith", "+32 470 11 11 11", "john@example.com", 6, Now.AddDays(2), 120, Guid.NewGuid(), "VIP", later);

        reservation.CustomerName.Should().Be("John Smith");
        reservation.PartySize.Should().Be(6);
        reservation.UpdatedAtUtc.Should().Be(later);
        reservation.CreatedAtUtc.Should().Be(Now, because: "CreatedAt never changes after creation.");
    }

    [Fact]
    public void UpdateDetails_StatusCancelled_ThrowsDomainRuleViolationException()
    {
        var reservation = CreateValid();
        reservation.Cancel(Now.AddMinutes(5));

        var act = () => reservation.UpdateDetails(
            "John Smith", "+32 470 11 11 11", null, 6, Now.AddDays(2), 120, null, null, Now.AddHours(1));

        act.Should().Throw<DomainRuleViolationException>(
            because: "editing an already-cancelled reservation is a genuine business-rule violation, same category as Table.Deactivate()'s guard.");
    }

    [Fact]
    public void UpdateDetails_PartySizeZero_ThrowsArgumentOutOfRangeException()
    {
        var reservation = CreateValid();

        var act = () => reservation.UpdateDetails(
            "Jane Doe", "+32 470 00 00 00", null, 0, Now.AddDays(1), 90, null, null, Now.AddHours(1));

        act.Should().Throw<ArgumentOutOfRangeException>(because: "UpdateDetails re-validates the same field invariants as Create.");
    }

    [Fact]
    public void Cancel_StatusConfirmed_SetsStatusCancelledAndBumpsUpdatedAt()
    {
        var reservation = CreateValid();
        var later = Now.AddMinutes(10);

        reservation.Cancel(later);

        reservation.Status.Should().Be(ReservationStatus.Cancelled);
        reservation.UpdatedAtUtc.Should().Be(later);
    }

    [Fact]
    public void Cancel_AlreadyCancelled_IsIdempotentNoOp()
    {
        var reservation = CreateValid();
        reservation.Cancel(Now.AddMinutes(5));

        var act = () => reservation.Cancel(Now.AddMinutes(10));

        act.Should().NotThrow(because: "re-cancelling an already-cancelled reservation is harmless, same leniency as Table.Deactivate().");
        reservation.Status.Should().Be(ReservationStatus.Cancelled);
    }

    [Fact]
    public void Cancel_StatusSeated_Succeeds()
    {
        var reservation = CreateValid();
        var tableId = Guid.NewGuid();
        reservation.MarkSeated(tableId, Now.AddMinutes(5));

        reservation.Cancel(Now.AddMinutes(10));

        reservation.Status.Should().Be(ReservationStatus.Cancelled,
            because: "Cancel is deliberately unguarded against the prior status — a Seated party can still be cancelled.");
    }

    [Fact]
    public void MarkSeated_StatusConfirmed_SetsStatusSeatedAndAssignsTable()
    {
        var reservation = CreateValid(tableId: Guid.NewGuid());
        var newTableId = Guid.NewGuid();
        var later = Now.AddMinutes(5);

        reservation.MarkSeated(newTableId, later);

        reservation.Status.Should().Be(ReservationStatus.Seated);
        reservation.TableId.Should().Be(newTableId, because: "seating always applies the caller's table, even reassigning from what Create/Update set.");
        reservation.UpdatedAtUtc.Should().Be(later);
    }

    [Fact]
    public void MarkSeated_TableIdEmpty_ThrowsArgumentException()
    {
        var reservation = CreateValid();

        var act = () => reservation.MarkSeated(Guid.Empty, Now.AddMinutes(5));

        act.Should().Throw<ArgumentException>(because: "BR-04 — a reservation cannot be marked Seated without an assigned table.");
        reservation.Status.Should().Be(ReservationStatus.Confirmed, because: "a rejected seat attempt must not partially apply.");
    }

    [Theory]
    [InlineData(ReservationStatus.Seated)]
    [InlineData(ReservationStatus.Cancelled)]
    [InlineData(ReservationStatus.NoShow)]
    public void MarkSeated_StatusNotConfirmed_ThrowsDomainRuleViolationException(ReservationStatus status)
    {
        var reservation = CreateValid();
        SetStatus(reservation, status);

        var act = () => reservation.MarkSeated(Guid.NewGuid(), Now.AddMinutes(5));

        act.Should().Throw<DomainRuleViolationException>(
            because: "only a Confirmed reservation can be seated, same self-contained-invariant category as UpdateDetails' guard.");
    }

    [Fact]
    public void MarkSeated_AlreadySeatedAtTheSameTable_IsIdempotentNoOp()
    {
        var reservation = CreateValid();
        var tableId = Guid.NewGuid();
        var firstSeatAt = Now.AddMinutes(5);
        reservation.MarkSeated(tableId, firstSeatAt);

        var act = () => reservation.MarkSeated(tableId, Now.AddMinutes(10));

        act.Should().NotThrow(
            because: "an OperationId replay of the same seat request must succeed, not be rejected as an invalid transition (caught by a failing integration test, not by inspection).");
        reservation.Status.Should().Be(ReservationStatus.Seated);
        reservation.UpdatedAtUtc.Should().Be(firstSeatAt, because: "a no-op replay must not advance UpdatedAtUtc either.");
    }

    [Fact]
    public void MarkSeated_AlreadySeatedAtADifferentTable_ThrowsDomainRuleViolationException()
    {
        var reservation = CreateValid();
        reservation.MarkSeated(Guid.NewGuid(), Now.AddMinutes(5));

        var act = () => reservation.MarkSeated(Guid.NewGuid(), Now.AddMinutes(10));

        act.Should().Throw<DomainRuleViolationException>(
            because: "a different tableId while already Seated is a genuine conflict, not a replay — reassignment isn't this endpoint's job.");
    }

    [Fact]
    public void MarkNoShow_StatusConfirmed_SetsStatusNoShowAndBumpsUpdatedAt()
    {
        var reservation = CreateValid();
        var later = Now.AddMinutes(10);

        reservation.MarkNoShow(later);

        reservation.Status.Should().Be(ReservationStatus.NoShow);
        reservation.UpdatedAtUtc.Should().Be(later);
    }

    [Fact]
    public void MarkNoShow_TableIdAssigned_LeavesTableIdUntouched()
    {
        var tableId = Guid.NewGuid();
        var reservation = CreateValid(tableId: tableId);

        reservation.MarkNoShow(Now.AddMinutes(10));

        reservation.TableId.Should().Be(tableId,
            because: "unlike Cancel/MarkNoShow's table-*release*, the historical assignment on the record itself is left untouched.");
    }

    [Fact]
    public void MarkNoShow_AlreadyNoShow_IsIdempotentNoOp()
    {
        var reservation = CreateValid();
        reservation.MarkNoShow(Now.AddMinutes(5));

        var act = () => reservation.MarkNoShow(Now.AddMinutes(10));

        act.Should().NotThrow(because: "re-marking an already-no-show reservation is harmless, same leniency as Cancel.");
        reservation.Status.Should().Be(ReservationStatus.NoShow);
    }

    [Fact]
    public void MarkNoShow_StatusSeated_Succeeds()
    {
        var reservation = CreateValid();
        reservation.MarkSeated(Guid.NewGuid(), Now.AddMinutes(5));

        var act = () => reservation.MarkNoShow(Now.AddMinutes(10));

        act.Should().NotThrow(because: "MarkNoShow is deliberately unguarded against the prior status, symmetric with Cancel.");
        reservation.Status.Should().Be(ReservationStatus.NoShow);
    }

    /// <summary>No public mutator can reach every status combination this file needs to set up
    /// as a starting point (e.g. Cancelled before testing MarkSeated's guard) — reflection is
    /// the same deliberate technique <c>TableTests.SetStatus</c> already uses.</summary>
    private static void SetStatus(Reservation reservation, ReservationStatus status) =>
        typeof(Reservation).GetProperty(nameof(Reservation.Status))!.SetValue(reservation, status);
}
