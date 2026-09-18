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
                npgsql.MigrationsHistoryTable("__ef_migrations_history", "tables")));

        services.AddScoped<ISectionsData, SectionsData>();
        services.AddScoped<ITablesData, TablesData>();
        services.AddScoped<ITableGroupsData, TableGroupsData>();
        services.AddScoped<ITableAvailabilityLookup, TableAvailabilityLookup>();
        services.AddScoped<IAuditWriter, AuditWriter<TablesDbContext>>();

        return services;
    }
}
