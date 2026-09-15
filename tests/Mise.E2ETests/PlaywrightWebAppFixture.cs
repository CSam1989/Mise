using System.Net;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Mise.E2ETests;

/// <summary>
/// Runs Mise.Web on a real Kestrel socket (.NET 10's WebApplicationFactory.UseKestrel — see
/// https://github.com/dotnet/aspnetcore/issues/60758) so an actual browser can navigate to
/// it. The in-memory TestServer WebApplicationFactory normally substitutes cannot be
/// targeted by Playwright at all — there is no real socket to connect to.
/// </summary>
public sealed class PlaywrightWebAppFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    private IPlaywright? _playwright;

    public IBrowser Browser { get; private set; } = null!;
    public string BaseUrl { get; private set; } = "";

    public PlaywrightWebAppFixture() =>
        UseKestrel(options => options.Listen(IPAddress.Loopback, 0));

    async ValueTask IAsyncLifetime.InitializeAsync()
    {
        StartServer();

        var addresses = Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>();
        BaseUrl = addresses!.Addresses.First();

        _playwright = await Playwright.CreateAsync();
        Browser = await _playwright.Chromium.LaunchAsync();
    }

    public override async ValueTask DisposeAsync()
    {
        await Browser.CloseAsync();
        _playwright?.Dispose();
        await base.DisposeAsync();
    }
}

[CollectionDefinition(Name)]
public sealed class PlaywrightCollection : ICollectionFixture<PlaywrightWebAppFixture>
{
    public const string Name = "Playwright";
}
