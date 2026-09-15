using Mise.Modules.Reservations.Domain;

namespace Mise.UnitTests.Reservations;

public class ReservationTests
{
    [Fact]
    public void Create_PartySizeZero_ThrowsArgumentOutOfRangeException()
    {
        var act = () => Reservation.Create(Guid.NewGuid(), "Jane Doe", 0, DateTimeOffset.UtcNow);

        act.Should().Throw<ArgumentOutOfRangeException>(
            because: "the factory's one invariant (docs/plan.md Phase 2) rejects a non-positive party size.");
    }

    [Fact]
    public void Create_PartySizeNegative_ThrowsArgumentOutOfRangeException()
    {
        var act = () => Reservation.Create(Guid.NewGuid(), "Jane Doe", -1, DateTimeOffset.UtcNow);

        act.Should().Throw<ArgumentOutOfRangeException>(because: "a negative party size is as invalid as zero.");
    }

    [Fact]
    public void Create_PartySizeOne_Succeeds()
    {
        var reservation = Reservation.Create(Guid.NewGuid(), "Jane Doe", 1, DateTimeOffset.UtcNow);

        reservation.PartySize.Should().Be(1,
            because: "1 is the smallest valid party size — the boundary just past the invariant this factory enforces.");
        reservation.Status.Should().Be(ReservationStatus.Confirmed, because: "Confirmed is the only status Phase 2 scopes in.");
    }

    [Fact]
    public void Create_CustomerNameEmpty_ThrowsArgumentException()
    {
        var act = () => Reservation.Create(Guid.NewGuid(), "   ", 2, DateTimeOffset.UtcNow);

        act.Should().Throw<ArgumentException>(
            because: "Guard.Against.NullOrWhiteSpace backstops the customer name at the Domain boundary.");
    }
}
