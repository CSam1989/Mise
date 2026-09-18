using System.Text;
using FluentValidation;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Tokens;
using Mise.ApiService;
using Mise.ApiService.Reservations;
using Mise.ApiService.Scheduling;
using Mise.ApiService.Staff;
using Mise.ApiService.Tables;
using Mise.Modules.Reservations.Application;
using Mise.Modules.Reservations.Application.CancelReservation;
using Mise.Modules.Reservations.Application.CreateReservation;
using Mise.Modules.Reservations.Application.UpdateReservation;
using Mise.Modules.Reservations.Infrastructure;
using Mise.Modules.Scheduling.Application.CreateServicePeriod;
using Mise.Modules.Scheduling.Application.DeleteServicePeriod;
using Mise.Modules.Scheduling.Application.UpdateServicePeriod;
using Mise.Modules.Scheduling.Infrastructure;
using Mise.Modules.StaffIdentity.Application.Login;
using Mise.Modules.StaffIdentity.Application.RegisterStaff;
using Mise.Modules.StaffIdentity.Domain;
using Mise.Modules.StaffIdentity.Infrastructure;
using Mise.Modules.Tables.Application.CreateSection;
using Mise.Modules.Tables.Application.CreateTable;
using Mise.Modules.Tables.Application.CreateTableGroup;
using Mise.Modules.Tables.Application.DeactivateSection;
using Mise.Modules.Tables.Application.DeactivateTable;
using Mise.Modules.Tables.Application.UpdateSection;
using Mise.Modules.Tables.Application.UpdateTable;
using Mise.Modules.Tables.Infrastructure;
using Mise.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);

// Add service defaults & Aspire client integrations.
builder.AddServiceDefaults();

// Add services to the container.
// Order matters: handlers run in registration order, first-to-return-true wins.
// ValidationExceptionHandler only claims ValidationException; GlobalExceptionHandler is the
// catch-all every other exception falls through to (logged, generic 500 — never a leaked
// stack trace or exception message in the response).
builder.Services.AddExceptionHandler<ValidationExceptionHandler>();
builder.Services.AddExceptionHandler<PreconditionRequiredExceptionHandler>();
builder.Services.AddExceptionHandler<ConcurrencyConflictExceptionHandler>();
builder.Services.AddExceptionHandler<DomainRuleViolationExceptionHandler>();
builder.Services.AddExceptionHandler<ReservationOverlapExceptionHandler>();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();

builder.Services.AddSingleton(TimeProvider.System);

// JWT-bearer validation — unchanged in shape since Phase 2's placeholder spine (CLAUDE.md:
// "the validation side already wired here" survives StaffIdentity landing). Only the minting
// side moved, from Mise.Web's fixed system identity to StaffIdentity's real per-staff tokens
// (JwtTokenIssuer). The signing key stays a secret Aspire parameter either way, never
// hardcoded (see Mise.AppHost's "jwt-signing-key").
var jwtSigningKey = builder.Configuration[JwtAuthDefaults.SigningKeyConfigKey]
    ?? throw new InvalidOperationException(
        $"Configuration key '{JwtAuthDefaults.SigningKeyConfigKey}' is required — run via `aspire run` or set it explicitly.");

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidIssuer = JwtAuthDefaults.Issuer,
            ValidAudience = JwtAuthDefaults.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSigningKey)),
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
        };
    });

// Global fallback policy: authenticated by default. Endpoints opt out with AllowAnonymous
// (Mise.ServiceDefaults' /health and /alive, plus /api/auth/login) rather than opting in one
// by one. "FloorStaff" accepts either role (ADR-001 §Auth: "Manager policy implies Floor
// Staff permissions" — RequireRole is an OR); "Manager" accepts only Manager.
builder.Services.AddAuthorizationBuilder()
    .AddPolicy("FloorStaff", policy => policy.RequireRole(nameof(StaffRole.FloorStaff), nameof(StaffRole.Manager)))
    .AddPolicy("Manager", policy => policy.RequireRole(nameof(StaffRole.Manager)))
    .SetFallbackPolicy(new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build());

var reservationsConnectionString = builder.Configuration.GetConnectionString("misedb")
    ?? throw new InvalidOperationException("Connection string 'misedb' is not configured.");
builder.Services.AddReservationsPersistence(reservationsConnectionString);
builder.Services.Configure<ReservationDefaultsOptions>(builder.Configuration.GetSection("Reservations"));
builder.Services.AddScoped<CreateReservationCommandHandler>();
builder.Services.AddScoped<IValidator<CreateReservationCommand>, CreateReservationCommandValidator>();
builder.Services.AddScoped<UpdateReservationCommandHandler>();
builder.Services.AddScoped<IValidator<UpdateReservationCommand>, UpdateReservationCommandValidator>();
builder.Services.AddScoped<CancelReservationCommandHandler>();

builder.Services.AddStaffIdentityPersistence(reservationsConnectionString);
builder.Services.AddStaffIdentityJwtIssuer(jwtSigningKey);
builder.Services.AddScoped<LoginCommandHandler>();
builder.Services.AddScoped<IValidator<LoginCommand>, LoginCommandValidator>();
builder.Services.AddScoped<RegisterStaffCommandHandler>();
builder.Services.AddScoped<IValidator<RegisterStaffCommand>, RegisterStaffCommandValidator>();

builder.Services.AddTablesPersistence(reservationsConnectionString);
builder.Services.AddScoped<CreateSectionCommandHandler>();
builder.Services.AddScoped<IValidator<CreateSectionCommand>, CreateSectionCommandValidator>();
builder.Services.AddScoped<UpdateSectionCommandHandler>();
builder.Services.AddScoped<IValidator<UpdateSectionCommand>, UpdateSectionCommandValidator>();
builder.Services.AddScoped<DeactivateSectionCommandHandler>();
builder.Services.AddScoped<CreateTableCommandHandler>();
builder.Services.AddScoped<IValidator<CreateTableCommand>, CreateTableCommandValidator>();
builder.Services.AddScoped<UpdateTableCommandHandler>();
builder.Services.AddScoped<IValidator<UpdateTableCommand>, UpdateTableCommandValidator>();
builder.Services.AddScoped<DeactivateTableCommandHandler>();
builder.Services.AddScoped<CreateTableGroupCommandHandler>();
builder.Services.AddScoped<IValidator<CreateTableGroupCommand>, CreateTableGroupCommandValidator>();

builder.Services.AddSchedulingPersistence(reservationsConnectionString);
builder.Services.AddScoped<CreateServicePeriodCommandHandler>();
builder.Services.AddScoped<IValidator<CreateServicePeriodCommand>, CreateServicePeriodCommandValidator>();
builder.Services.AddScoped<UpdateServicePeriodCommandHandler>();
builder.Services.AddScoped<IValidator<UpdateServicePeriodCommand>, UpdateServicePeriodCommandValidator>();
builder.Services.AddScoped<DeleteServicePeriodCommandHandler>();

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
app.MapAuthEndpoints();
app.MapStaffEndpoints();
app.MapSectionsEndpoints();
app.MapTablesEndpoints();
app.MapTableGroupsEndpoints();
app.MapServicePeriodsEndpoints();

app.MapDefaultEndpoints();

app.Run();

// Referenced by integration tests via WebApplicationFactory<Program>.
public partial class Program;
