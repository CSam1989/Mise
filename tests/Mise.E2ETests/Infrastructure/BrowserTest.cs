namespace Mise.E2ETests.Infrastructure;

/// <summary>
/// Gives each test isolated browser contexts (optionally pre-signed-in via a saved storage state) and,
/// on teardown, saves a screenshot and Playwright trace per context to playwright-traces/ — CI uploads
/// that folder when the job fails.
/// </summary>
public abstract class BrowserTest(IBrowserHost host) : IAsyncLifetime
{
    private readonly List<(IBrowserContext Context, IPage Page)> _sessions = [];

    protected string BaseUrl => host.BaseUrl;

    protected async Task<IPage> OpenPageAsync(string? storageState = null, string locale = "en", int width = 1280, int height = 800)
    {
        var context = await host.Browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = host.BaseUrl,
            Locale = locale,
            ViewportSize = new ViewportSize { Width = width, Height = height },
            StorageState = storageState,
        });
        await context.Tracing.StartAsync(new TracingStartOptions { Screenshots = true, Snapshots = true, Sources = true });

        var page = await context.NewPageAsync();
        _sessions.Add((context, page));
        return page;
    }

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        var testName = TestContext.Current.TestMethod?.MethodName ?? GetType().Name;
        Directory.CreateDirectory("playwright-traces");

        for (var i = 0; i < _sessions.Count; i++)
        {
            var (context, page) = _sessions[i];
            var name = _sessions.Count == 1 ? testName : $"{testName}-{i + 1}";
            await page.ScreenshotAsync(new PageScreenshotOptions { Path = Path.Combine("playwright-traces", $"{name}.png") });
            await context.Tracing.StopAsync(new TracingStopOptions { Path = Path.Combine("playwright-traces", $"{name}.zip") });
            await context.DisposeAsync();
        }

        GC.SuppressFinalize(this);
    }
}
