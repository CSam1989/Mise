using Bunit;
using Mise.UI.Components;
using Mise.UI.Components.Shell;

namespace Mise.Client.UnitTests.Shell;

public class ShellClockTests : MiseComponentTestContext
{
    private static readonly TimeZoneInfo Brussels = TimeZoneInfo.FindSystemTimeZoneById("Europe/Brussels");

    [Fact]
    public void Render_ShowsRestaurantLocalDateAndTime_NotUtc()
    {
        var cut = Render<ShellClock>(p => p.Add(c => c.TimeZone, Brussels));

        cut.Find($"[data-testid={TestIds.ShellDate}]").TextContent.Should().Be("Wed 23 Sep");
        cut.Find($"[data-testid={TestIds.ShellClock}]").TextContent.Should().Be("19:04",
            because: "17:04 UTC is 19:04 in Brussels during CEST; staff read the restaurant's wall clock, not UTC.");
    }

    [Fact]
    public void Render_LocalMidnightCrossing_ShowsTheLocalDay()
    {
        Time.SetUtcNow(new DateTimeOffset(2026, 9, 23, 22, 30, 0, TimeSpan.Zero));

        var cut = Render<ShellClock>(p => p.Add(c => c.TimeZone, Brussels));

        cut.Find($"[data-testid={TestIds.ShellDate}]").TextContent.Should().Be("Thu 24 Sep",
            because: "22:30 UTC is already 00:30 the next day in Brussels.");
    }

    [Fact]
    public void AdvanceToNextMinute_UpdatesTheClock()
    {
        var cut = Render<ShellClock>(p => p.Add(c => c.TimeZone, Brussels));

        Time.Advance(TimeSpan.FromSeconds(30));

        cut.WaitForAssertion(() => cut.Find($"[data-testid={TestIds.ShellClock}]").TextContent.Should().Be("19:05"));
    }

    [Fact]
    public void AdvanceWithinTheSameMinute_DoesNotChangeTheClock()
    {
        var cut = Render<ShellClock>(p => p.Add(c => c.TimeZone, Brussels));

        Time.Advance(TimeSpan.FromSeconds(29));

        cut.Find($"[data-testid={TestIds.ShellClock}]").TextContent.Should().Be("19:04",
            because: "the first tick is aligned to the next minute boundary, not a fixed 60 s after render.");
    }

    [Fact]
    public void Render_DutchCulture_UsesDutchDayAndMonth()
    {
        UseCulture("nl-BE");

        var cut = Render<ShellClock>(p => p.Add(c => c.TimeZone, Brussels));

        cut.Find($"[data-testid={TestIds.ShellDate}]").TextContent.Should().StartWith("wo 23 sep");
    }
}
