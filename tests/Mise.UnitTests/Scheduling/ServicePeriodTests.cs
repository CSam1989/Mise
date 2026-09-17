using Mise.Modules.Scheduling.Domain;

namespace Mise.UnitTests.Scheduling;

public class ServicePeriodTests
{
    private static readonly DateOnly Date = new(2026, 9, 17);

    [Fact]
    public void Create_LabelEmpty_ThrowsArgumentException()
    {
        var act = () => ServicePeriod.Create(
            Guid.NewGuid(), Date, "   ", new TimeOnly(12, 0), new TimeOnly(14, 30), endsNextDay: false, isClosed: false);

        act.Should().Throw<ArgumentException>(because: "Guard.Against.NullOrWhiteSpace backstops the label.");
    }

    [Fact]
    public void Create_SameDayEndTimeBeforeStartTime_ThrowsArgumentOutOfRangeException()
    {
        var act = () => ServicePeriod.Create(
            Guid.NewGuid(), Date, "Lunch", new TimeOnly(14, 0), new TimeOnly(12, 0), endsNextDay: false, isClosed: false);

        act.Should().Throw<ArgumentOutOfRangeException>(
            because: "a same-day window with EndTime before StartTime has no meaning.");
    }

    [Fact]
    public void Create_SameDayEndTimeEqualsStartTime_ThrowsArgumentOutOfRangeException()
    {
        var act = () => ServicePeriod.Create(
            Guid.NewGuid(), Date, "Lunch", new TimeOnly(12, 0), new TimeOnly(12, 0), endsNextDay: false, isClosed: false);

        act.Should().Throw<ArgumentOutOfRangeException>(
            because: "a same-day window can't have zero length — the boundary just past the invariant.");
    }

    [Fact]
    public void Create_EndsNextDayEndTimeBeforeStartTime_Succeeds()
    {
        var dinner = ServicePeriod.Create(
            Guid.NewGuid(), Date, "Dinner", new TimeOnly(18, 0), new TimeOnly(1, 0), endsNextDay: true, isClosed: false);

        dinner.EndsNextDay.Should().BeTrue(
            because: "docs/plan.md correction #9 — Dinner 18:00-01:00 must be representable.");
        dinner.EndTime.Should().Be(new TimeOnly(1, 0));
    }

    [Fact]
    public void Create_EndsNextDayEndTimeEqualsStartTime_SucceedsAsFullDaySpan()
    {
        var closedAllDay = ServicePeriod.Create(
            Guid.NewGuid(), Date, "Closed", new TimeOnly(0, 0), new TimeOnly(0, 0), endsNextDay: true, isClosed: true);

        closedAllDay.IsClosed.Should().BeTrue();
        closedAllDay.EndTime.Should().Be(closedAllDay.StartTime,
            because: "00:00-00:00 with EndsNextDay true is the whole-day-closed representation, not a zero-length window.");
    }

    [Fact]
    public void UpdateDetails_ValidInput_UpdatesAllFields()
    {
        var servicePeriod = ServicePeriod.Create(
            Guid.NewGuid(), Date, "Lunch", new TimeOnly(12, 0), new TimeOnly(14, 30), endsNextDay: false, isClosed: false);

        servicePeriod.UpdateDetails(
            Date.AddDays(1), "Brunch", new TimeOnly(11, 0), new TimeOnly(15, 0), endsNextDay: false, isClosed: false);

        servicePeriod.Date.Should().Be(Date.AddDays(1));
        servicePeriod.Label.Should().Be("Brunch");
        servicePeriod.StartTime.Should().Be(new TimeOnly(11, 0));
        servicePeriod.EndTime.Should().Be(new TimeOnly(15, 0));
    }

    [Fact]
    public void UpdateDetails_LabelEmpty_ThrowsArgumentException()
    {
        var servicePeriod = ServicePeriod.Create(
            Guid.NewGuid(), Date, "Lunch", new TimeOnly(12, 0), new TimeOnly(14, 30), endsNextDay: false, isClosed: false);

        var act = () => servicePeriod.UpdateDetails(
            Date, "", new TimeOnly(12, 0), new TimeOnly(14, 30), endsNextDay: false, isClosed: false);

        act.Should().Throw<ArgumentException>(because: "UpdateDetails re-validates the same invariant Create enforces.");
    }
}
