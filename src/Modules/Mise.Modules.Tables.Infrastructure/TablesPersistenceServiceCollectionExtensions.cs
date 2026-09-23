using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Mise.Modules.Tables.Application.Ports;
using Mise.Modules.Tables.Contracts;
using Mise.Modules.Tables.Infrastructure.Persistence;
using Mise.SharedKernel.Infrastructure;
using Mise.SharedKernel.Persistence;

namespace Mise.Modules.Tables.Infrastructure;

/// <summary>
/// Single place the module's DbContext options (including the per-module migrations-history
/// table — plan.md's "four DbContexts sharing one Postgres database" gotcha) are configured,
/// so Mise.ApiService and Mise.MigrationService can't drift from each other.
/// </summary>
public static class TablesPersistenceServiceCollectionExtensions
{
    public static IServiceCollection AddTablesPersistence(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<TablesDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql =>
                    npgsql.MigrationsHistoryTable("__ef_migrations_history", "tables"))
                .AddInterceptors(new AuditCompletenessInterceptor()));

        services.AddScoped<ISectionsData, SectionsData>();
        services.AddScoped<ITablesData, TablesData>();
        services.AddScoped<ITableGroupsData, TableGroupsData>();
        services.AddScoped<ITableAvailabilityLookup, TableAvailabilityLookup>();
        services.AddKeyedScoped<IAuditWriter, AuditWriter<TablesDbContext>>(AuditWriterKeys.Tables);

        // ADR-007's cross-module event handlers (ReservationSeatedTableOccupiedHandler /
        // ReservationTableVacatedHandler) are deliberately NOT registered here: this extension
        // method is Tables' own persistence wiring, and "which other module's event this module
        // reacts to" is a cross-module composition concern — it belongs at Mise.ApiService's
        // composition root (Program.cs), alongside every other cross-module DI decision, not
        // buried inside one module's own extension method.

        return services;
    }
}
