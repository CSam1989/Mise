namespace Mise.UI.Components.Feedback;

public enum ToastKind
{
    Info,
    Error,
}

public sealed record ToastMessage(string Text, ToastKind Kind);

/// <summary>One toast at a time, replaced by the next, auto-dismissed after <see cref="DisplayDuration"/> — the mockup's behaviour.</summary>
public sealed class ToastService(TimeProvider timeProvider) : IDisposable
{
    public static readonly TimeSpan DisplayDuration = TimeSpan.FromMilliseconds(2400);

    private readonly Lock _gate = new();
    private ITimer? _timer;

    public event Action? Changed;

    public ToastMessage? Current { get; private set; }

    public void Show(string text, ToastKind kind = ToastKind.Info)
    {
        lock (_gate)
        {
            _timer?.Dispose();
            Current = new ToastMessage(text, kind);
            _timer = timeProvider.CreateTimer(_ => Dismiss(), null, DisplayDuration, Timeout.InfiniteTimeSpan);
        }

        Changed?.Invoke();
    }

    public void Dismiss()
    {
        lock (_gate)
        {
            if (Current is null)
            {
                return;
            }

            _timer?.Dispose();
            _timer = null;
            Current = null;
        }

        Changed?.Invoke();
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _timer?.Dispose();
        }
    }
}
