using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Mise.Modules.Reservations.Infrastructure;
using Mise.Modules.Reservations.Infrastructure.Persistence;

// One-shot worker: migrates every module's DbContext, then exits. Mise.AppHost waits on
// this (WaitForCompletion) before starting Mise.ApiService — schema-readiness is a
// precondition of serving traffic, not something the API host does for itself on boot
// (docs/plan.md's solution inventory). A second module's DbContext gets its own
// AddXPersistence(...) call added here in the same commit that adds the module.
var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();

var connectionString = builder.Configuration.GetConnectionString("misedb")
    ?? throw new InvalidOperationException("Connection string 'misedb' is not configured.");

builder.Services.AddReservationsPersistence(connectionString);

using var host = builder.Build();
using var scope = host.Services.CreateScope();

// Plain ILogger calls, not [LoggerMessage] — this runs once per process start, so the
// source-generator's performance win doesn't apply (CLAUDE.md's Logging carve-out for
// composition-root startup diagnostics).
var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

var reservationsDb = scope.ServiceProvider.GetRequiredService<ReservationsDbContext>();
logger.LogInformation("Applying migrations for {DbContext}...", nameof(ReservationsDbContext));
await reservationsDb.Database.MigrateAsync();
logger.LogInformation("Migrations for {DbContext} applied.", nameof(ReservationsDbContext));
