using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Mise.Modules.Reservations.Infrastructure;
using Mise.Modules.Reservations.Infrastructure.Persistence;
using Mise.Modules.Scheduling.Infrastructure;
using Mise.Modules.Scheduling.Infrastructure.Persistence;
using Mise.Modules.StaffIdentity.Infrastructure;
using Mise.Modules.StaffIdentity.Infrastructure.Persistence;
using Mise.Modules.Tables.Infrastructure;
using Mise.Modules.Tables.Infrastructure.Persistence;

// One-shot worker: migrates every module's DbContext, then exits. Mise.AppHost waits on
// this (WaitForCompletion) before starting Mise.ApiService — schema-readiness is a
// precondition of serving traffic, not something the API host does for itself on boot
// (docs/plan.md's solution inventory). A second module's DbContext gets its own
// AddXPersistence(...) call added here in the same commit that adds the module.
var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();

var connectionString = builder.Configuration.GetConnectionString("misedb")
    ?? throw new InvalidOperationException("Connection string 'misedb' is not configured.");

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddReservationsPersistence(connectionString);
builder.Services.AddStaffIdentityPersistence(connectionString);
builder.Services.AddTablesPersistence(connectionString);
builder.Services.AddSchedulingPersistence(connectionString);

using var host = builder.Build();
using var scope = host.Services.CreateScope();

// Plain ILogger calls, not [LoggerMessage] — this runs once per process start, so the
// source-generator's performance win doesn't apply (CLAUDE.md's Logging carve-out for
// composition-root startup diagnostics).
var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

try
{
    // Order matters: Reservations owns shared.processed_operation/audit_log_entry's DDL (it
    // was first to need them); StaffIdentity's own migration deliberately excludes them from
    // its migration (Mise.SharedKernel.Persistence's isOwner flag) and only reads/writes the
    // physical tables Reservations' migration creates. Migrating StaffIdentity first would
    // have it query tables that don't exist yet.
    var reservationsDb = scope.ServiceProvider.GetRequiredService<ReservationsDbContext>();
    logger.LogInformation("Applying migrations for {DbContext}...", nameof(ReservationsDbContext));
    await reservationsDb.Database.MigrateAsync();
    logger.LogInformation("Migrations for {DbContext} applied.", nameof(ReservationsDbContext));

    var staffIdentityDb = scope.ServiceProvider.GetRequiredService<StaffIdentityDbContext>();
    logger.LogInformation("Applying migrations for {DbContext}...", nameof(StaffIdentityDbContext));
    await staffIdentityDb.Database.MigrateAsync();
    logger.LogInformation("Migrations for {DbContext} applied.", nameof(StaffIdentityDbContext));

    // Tables is a second non-owner of shared.processed_operation/audit_log_entry (same
    // isOwner: false pattern as StaffIdentity) — it only needs to run after Reservations, so
    // its position relative to StaffIdentity above is arbitrary.
    var tablesDb = scope.ServiceProvider.GetRequiredService<TablesDbContext>();
    logger.LogInformation("Applying migrations for {DbContext}...", nameof(TablesDbContext));
    await tablesDb.Database.MigrateAsync();
    logger.LogInformation("Migrations for {DbContext} applied.", nameof(TablesDbContext));

    // Scheduling is a third non-owner of shared.processed_operation/audit_log_entry (same
    // isOwner: false pattern as StaffIdentity/Tables) — it only needs to run after
    // Reservations, so its position relative to the other two non-owners is arbitrary.
    var schedulingDb = scope.ServiceProvider.GetRequiredService<SchedulingDbContext>();
    logger.LogInformation("Applying migrations for {DbContext}...", nameof(SchedulingDbContext));
    await schedulingDb.Database.MigrateAsync();
    logger.LogInformation("Migrations for {DbContext} applied.", nameof(SchedulingDbContext));

    // Bootstraps the one account that can ever sign in before any Manager exists to register
    // further staff — RegisterStaffCommandHandler's endpoint is Manager-only, so without this
    // nobody could reach it (docs/Phase-3-Manual-Test-Checklist.md's one-time setup covers
    // setting the password secret). Idempotent — safe on every restart, not just the first.
    var seedManagerPassword = builder.Configuration["StaffIdentity:SeedManager:Password"]
        ?? throw new InvalidOperationException(
            "Configuration key 'StaffIdentity:SeedManager:Password' is required — set it via `dotnet user-secrets set Parameters:seed-manager-password <value> --project src/Mise.AppHost`.");
    var seedManagerUsername = builder.Configuration["StaffIdentity:SeedManager:Username"] ?? "manager";

    var seeder = scope.ServiceProvider.GetRequiredService<StaffIdentitySeeder>();
    var created = await seeder.EnsureManagerExistsAsync(
        seedManagerUsername, seedManagerPassword, "Restaurant Manager", CancellationToken.None);
    logger.LogInformation(
        "Seed Manager account {Username}: {Outcome}.", seedManagerUsername, created ? "created" : "already existed");
}
catch (Exception ex)
{
    // Without this, a failure here is only ever visible as a raw, unstructured stderr dump
    // from the runtime's default unhandled-exception handling — invisible to whatever's
    // actually watching Aspire's/production's structured log stream. CLAUDE.md's Critical
    // guidance applies exactly here: the process itself can't do its job. Rethrown afterward
    // so the process still exits non-zero — Mise.AppHost's WaitForCompletion(migrations) must
    // keep seeing this as a failure, not a silently-swallowed one.
    logger.LogCritical(ex, "Mise.MigrationService failed to migrate or seed the database.");
    throw;
}
