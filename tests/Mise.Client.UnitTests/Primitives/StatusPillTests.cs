using Bunit;
using Mise.UI.Abstractions;
using Mise.UI.Components;
using Mise.UI.Components.Primitives;

namespace Mise.Client.UnitTests.Primitives;

public class StatusPillTests : MiseComponentTestContext
{
    [Theory]
    [InlineData(TableStatus.Available, "Available", "mise-status-available")]
    [InlineData(TableStatus.Reserved, "Reserved", "mise-status-reserved")]
    [InlineData(TableStatus.Occupied, "Occupied", "mise-status-occupied")]
    [InlineData(TableStatus.NeedsCleaning, "Needs cleaning", "mise-status-needscleaning")]
    [InlineData(TableStatus.Blocked, "Blocked", "mise-status-blocked")]
    public void Render_TableStatus_ShowsLabelAndColourSet(TableStatus status, string label, string cssClass)
    {
        var cut = Render<StatusPill>(p => p.Add(c => c.Status, status));

        var pill = cut.Find($"[data-testid={TestIds.StatusPill}]");
        pill.TextContent.Should().Be(label);
        pill.ClassList.Should().Contain(cssClass, because: "each table status maps to exactly one of the mockup's five colour sets.");
    }

    [Theory]
    [InlineData(ReservationStatus.Confirmed, "Confirmed", "mise-status-confirmed")]
    [InlineData(ReservationStatus.Seated, "Seated", "mise-status-seated")]
    [InlineData(ReservationStatus.Completed, "Completed", "mise-status-completed")]
    [InlineData(ReservationStatus.NoShow, "No-show", "mise-status-noshow")]
    [InlineData(ReservationStatus.Cancelled, "Cancelled", "mise-status-cancelled")]
    public void Render_ReservationStatus_ShowsLabelAndColourSet(ReservationStatus status, string label, string cssClass)
    {
        var cut = Render<StatusPill>(p => p.Add(c => c.Status, status));

        var pill = cut.Find($"[data-testid={TestIds.StatusPill}]");
        pill.TextContent.Should().Be(label);
        pill.ClassList.Should().Contain(cssClass, because: "each reservation status maps to exactly one of the mockup's five colour sets.");
    }

    [Fact]
    public void Render_DutchCulture_ShowsDutchLabel()
    {
        UseCulture("nl-BE");

        var cut = Render<StatusPill>(p => p.Add(c => c.Status, TableStatus.NeedsCleaning));

        cut.Find($"[data-testid={TestIds.StatusPill}]").TextContent.Should().Be("Af te ruimen",
            because: "nl-BE is the default culture and the mockup's T.nl value must be what staff see.");
    }

    [Fact]
    public void Render_UnsupportedEnum_Throws()
    {
        var act = () => Render<StatusPill>(p => p.Add(c => c.Status, DayOfWeek.Monday));

        act.Should().Throw<ArgumentException>(because: "only table and reservation statuses have a colour set; anything else is a programming error.");
    }
}
