namespace Mise.E2ETests.Infrastructure;

/// <summary>A running Mise.Web plus the browser pointed at it — what every <see cref="BrowserTest"/> needs from its fixture.</summary>
public interface IBrowserHost
{
    IBrowser Browser { get; }

    string BaseUrl { get; }
}
