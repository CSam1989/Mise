using System.Text;
using FluentValidation;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Tokens;
using Mise.ApiService;
using Mise.ApiService.Reservations;
using Mise.Modules.Reservations.Application.CreateReservation;
using Mise.Modules.Reservations.Infrastructure;
using Mise.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);

// Add service defaults & Aspire client integrations.
builder.AddServiceDefaults();

// Add services to the container.
builder.Services.AddExceptionHandler<ValidationExceptionHandler>();
builder.Services.AddProblemDetails();

builder.Services.AddSingleton(TimeProvider.System);

// Minimal placeholder JWT-bearer scheme (docs/plan.md Phase 2) — just enough for the
// unauthenticated-request-is-rejected seam to be real. StaffIdentity (Phase 3) replaces this
// with real sign-in; the signing key stays a secret Aspire parameter either way, never
// hardcoded (see Mise.AppHost's "jwt-signing-key").
var jwtSigningKey = builder.Configuration[PlaceholderAuthDefaults.SigningKeyConfigKey]
    ?? throw new InvalidOperationException(
        $"Configuration key '{PlaceholderAuthDefaults.SigningKeyConfigKey}' is required — run via `aspire run` or set it explicitly.");

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidIssuer = PlaceholderAuthDefaults.Issuer,
            ValidAudience = PlaceholderAuthDefaults.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSigningKey)),
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
        };
    });

// Global fallback policy: authenticated by default. Endpoints opt out with AllowAnonymous
// (Mise.ServiceDefaults' /health and /alive already do) rather than opting in one by one.
builder.Services.AddAuthorizationBuilder()
    .SetFallbackPolicy(new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build());

var reservationsConnectionString = builder.Configuration.GetConnectionString("misedb")
    ?? throw new InvalidOperationException("Connection string 'misedb' is not configured.");
builder.Services.AddReservationsPersistence(reservationsConnectionString);
builder.Services.AddScoped<CreateReservationCommandHandler>();
builder.Services.AddScoped<IValidator<CreateReservationCommand>, CreateReservationCommandValidator>();

// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

var app = builder.Build();

// Configure the HTTP request pipeline.
app.UseExceptionHandler();

app.UseAuthentication();
app.UseAuthorization();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapGet("/", () => "Mise API service is running.");

app.MapReservationsEndpoints();

app.MapDefaultEndpoints();

app.Run();

// Referenced by integration tests via WebApplicationFactory<Program>.
public partial class Program;
