using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Localization;
using Mise.UI.Abstractions;
using Mise.UI.Components;
using Mise.Web;
using Mise.Web.Components;
using Mise.Web.Services;

var builder = WebApplication.CreateBuilder(args);

// Add service defaults & Aspire client integrations.
builder.AddServiceDefaults();

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents(options =>
        // Explicit, not just relying on the (already-safe) framework default: an unhandled
        // circuit exception must never show a Floor Staff member a raw stack trace in
        // production. MainLayout.razor's #blazor-error-ui banner ("An unhandled error has
        // occurred. Reload.") is the generic message shown instead — the same safety-net role
        // ValidationExceptionHandler/GlobalExceptionHandler play on the API side.
        options.DetailedErrors = builder.Environment.IsDevelopment());

builder.Services.AddOutputCache();

builder.Services.AddSingleton(TimeProvider.System);

// Cookie auth is this host's own sign-in (ADR-004: Mise.Web authenticates its browser
// sessions independently of the bearer token it holds server-side for calling the API).
// LoginPath covers both the interactive [Authorize] redirect (Routes.razor's
// AuthorizeRouteView -> RedirectToLogin) and an anonymous request to an endpoint-routed page.
builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/login";
        options.AccessDeniedPath = "/login";
    });
builder.Services.AddAuthorizationBuilder()
    .AddPolicy(StaffPolicies.FloorStaff, policy => policy.RequireRole(StaffRoles.FloorStaff, StaffRoles.Manager))
    .AddPolicy(StaffPolicies.Manager, policy => policy.RequireRole(StaffRoles.Manager));
builder.Services.AddCascadingAuthenticationState();

// nl-BE is the charter's default (NFR-08); en is the other supported culture. The RCL's own
// strings (IStringLocalizer<UiStrings>) ship both cultures; the culture switcher is Phase 11.
builder.Services.AddLocalization(options => options.ResourcesPath = "Resources");

builder.Services
    .AddOptions<RestaurantOptions>()
    .BindConfiguration(RestaurantOptions.SectionName)
    .ValidateDataAnnotations()
    .Validate(options => TimeZoneInfo.TryFindSystemTimeZoneById(options.TimeZoneId, out _), "Restaurant:TimeZoneId is not a known time zone.")
    .ValidateOnStart();

builder.Services.AddMiseUiComponents();

// The signed-in staff member's API token, held server-side only — never a cookie or
// localStorage value (ADR-004). Singleton: it must outlive any one circuit so a reconnect
// (or the redirect right after login, which starts a brand new circuit) still finds it.
builder.Services.AddSingleton<IStaffSessionTokenCache, StaffSessionTokenCache>();

// Typed HTTP clients implementing Mise.UI.Abstractions (ADR-004: this host is an API client,
// not an in-process caller of the Application layer). HttpStaffAuthClient's call is
// anonymous (logging in is how a token is obtained at all); HttpReservationsClient attaches
// the caller's own bearer token itself — see its doc comment for why that can't be a
// DelegatingHandler here.
builder.Services.AddHttpClient<IStaffAuthClient, HttpStaffAuthClient>(client =>
    client.BaseAddress = new Uri("https+http://apiservice"));
builder.Services.AddHttpClient<IReservationsClient, HttpReservationsClient>(client =>
    client.BaseAddress = new Uri("https+http://apiservice"));

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();

    // The /design gallery (and the Preview* fakes it will host) never exists outside Development.
    app.MapWhen(
        context => context.Request.Path.StartsWithSegments("/design"),
        branch => branch.Run(context =>
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return Task.CompletedTask;
        }));
}

app.UseHttpsRedirection();

app.UseRequestLocalization(new RequestLocalizationOptions()
    .SetDefaultCulture("nl-BE")
    .AddSupportedCultures("nl-BE", "en")
    .AddSupportedUICultures("nl-BE", "en"));

app.UseAuthentication();
app.UseAuthorization();

app.UseAntiforgery();

app.UseOutputCache();

app.MapStaticAssets();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// A plain GET, not a Blazor interactive action, for the same reason Login.razor's sign-in
// can't be: signing out needs a real HTTP response to clear the auth cookie.
app.MapGet("/logout", (HttpContext httpContext, IStaffSessionTokenCache tokenCache) =>
{
    var staffIdClaim = httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
    if (staffIdClaim is not null && Guid.TryParse(staffIdClaim, out var staffId))
    {
        tokenCache.Remove(staffId);
    }

    return Results.SignOut(new AuthenticationProperties { RedirectUri = "/" }, [CookieAuthenticationDefaults.AuthenticationScheme]);
}).RequireAuthorization();

app.MapDefaultEndpoints();

app.Run();

// Referenced by E2E tests via WebApplicationFactory<Program>.
public partial class Program;
