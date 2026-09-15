using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Mise.E2ETests;

/// <summary>
/// Generic real-Kestrel-socket factory, shared by the ApiService and Web hosts
/// ReservationsE2EFixture boots — the same .NET 10 WebApplicationFactory.UseKestrel
/// mechanism PlaywrightWebAppFixture uses for Mise.Web alone, generalized to any host so
/// Playwright's browser and Mise.Web's own outgoing HTTP calls can both reach a real socket.
/// </summary>
internal sealed class KestrelFactory<TEntryPoint> : WebApplicationFactory<TEntryPoint>
    where TEntryPoint : class
{
    private readonly Action<IWebHostBuilder> _configure;

    public KestrelFactory(Action<IWebHostBuilder> configure)
    {
        _configure = configure;
        UseKestrel(options => options.Listen(IPAddress.Loopback, 0));
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder) => _configure(builder);

    public string Start()
    {
        StartServer();
        var addresses = Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>();
        return addresses!.Addresses.First();
    }
}
