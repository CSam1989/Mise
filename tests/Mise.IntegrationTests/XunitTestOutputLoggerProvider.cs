using Microsoft.Extensions.Logging;

namespace Mise.IntegrationTests;

/// <summary>
/// Forwards <see cref="ILogger"/> output to xUnit v3's <c>TestContext.Current.TestOutputHelper</c>.
/// <see cref="WebTests"/> already configures Debug-level logging for the whole distributed app,
/// but until this existed nothing captured it, so a timeout there gave no clue which resource
/// actually stalled — the smoke test's whole reason to exist (does the real app graph start?)
/// was undebuggable when it failed. xUnit surfaces anything written here in the failing test's
/// own output, no separate log file or CI artifact needed.
/// </summary>
internal sealed class XunitTestOutputLoggerProvider : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => new XunitTestOutputLogger(categoryName);

    public void Dispose()
    {
    }

    private sealed class XunitTestOutputLogger(string categoryName) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            // Null outside an active test (e.g. a background continuation after the test's own
            // scope ended) — nothing to write to, and nothing worth throwing over.
            var output = TestContext.Current.TestOutputHelper;
            if (output is null)
            {
                return;
            }

            var message = $"[{logLevel}] {categoryName}: {formatter(state, exception)}";
            output.WriteLine(exception is null ? message : $"{message}{Environment.NewLine}{exception}");
        }
    }
}
