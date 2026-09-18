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
}
