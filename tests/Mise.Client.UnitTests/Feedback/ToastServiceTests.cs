using Microsoft.Extensions.Time.Testing;
using Mise.UI.Components.Feedback;

namespace Mise.Client.UnitTests.Feedback;

public class ToastServiceTests
{
    private readonly FakeTimeProvider _time = new();

    [Fact]
    public void Show_SetsCurrentAndRaisesChanged()
    {
        using var service = new ToastService(_time);
        var raised = 0;
        service.Changed += () => raised++;

        service.Show("Saved", ToastKind.Error);

        service.Current.Should().Be(new ToastMessage("Saved", ToastKind.Error));
        raised.Should().Be(1);
    }

    [Fact]
    public void Show_JustBeforeDisplayDuration_IsStillVisible()
    {
        using var service = new ToastService(_time);
        service.Show("Saved");

        _time.Advance(ToastService.DisplayDuration - TimeSpan.FromMilliseconds(1));

        service.Current.Should().NotBeNull(because: "a toast stays up for the full 2.4 s the mockup uses.");
    }

    [Fact]
    public void Show_AtDisplayDuration_AutoDismissesAndRaisesChanged()
    {
        using var service = new ToastService(_time);
        service.Show("Saved");
        var raised = 0;
        service.Changed += () => raised++;

        _time.Advance(ToastService.DisplayDuration);

        service.Current.Should().BeNull();
        raised.Should().Be(1);
    }

    [Fact]
    public void ShowTwice_ReplacesTheFirstAndRestartsTheTimer()
    {
        using var service = new ToastService(_time);
        service.Show("First");
        _time.Advance(TimeSpan.FromSeconds(2));

        service.Show("Second");
        _time.Advance(TimeSpan.FromSeconds(1));

        service.Current!.Text.Should().Be("Second", because: "the second toast gets its own full display window, not the remainder of the first's.");
    }

    [Fact]
    public void Dismiss_WhenNothingShown_DoesNotRaiseChanged()
    {
        using var service = new ToastService(_time);
        var raised = 0;
        service.Changed += () => raised++;

        service.Dismiss();

        raised.Should().Be(0);
    }
}
