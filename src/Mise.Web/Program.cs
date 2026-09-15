using Mise.UI.Abstractions;
using Mise.Web.Components;
using Mise.Web.Services;

var builder = WebApplication.CreateBuilder(args);

// Add service defaults & Aspire client integrations.
builder.AddServiceDefaults();

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddOutputCache();

// The typed HTTP client implementing Mise.UI.Abstractions (ADR-004: this host is an API
// client, not an in-process caller of the Application layer). PlaceholderAuthTokenHandler is
// Phase 2's stand-in for ADR-004's real sign-in flow — see its own doc comment.
builder.Services.AddTransient<PlaceholderAuthTokenHandler>();
builder.Services
    .AddHttpClient<IReservationsClient, HttpReservationsClient>(client =>
        client.BaseAddress = new Uri("https+http://apiservice"))
    .AddHttpMessageHandler<PlaceholderAuthTokenHandler>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseAntiforgery();

app.UseOutputCache();

app.MapStaticAssets();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapDefaultEndpoints();

app.Run();

// Referenced by E2E tests via WebApplicationFactory<Program>.
public partial class Program;
