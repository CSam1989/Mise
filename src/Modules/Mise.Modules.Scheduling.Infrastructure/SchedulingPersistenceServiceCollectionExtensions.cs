using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Mise.Modules.Scheduling.Application.Ports;
using Mise.Modules.Scheduling.Infrastructure.Persistence;
using Mise.SharedKernel.Infrastructure;
using Mise.SharedKernel.Persistence;

namespace Mise.Modules.Scheduling.Infrastructure;

/// <summary>
/// Single place the module's DbContext options (including the per-module migrations-history
/// table — plan.md's "four DbContexts sharing one Postgres database" gotcha) are configured,
/// so Mise.ApiService and Mise.MigrationService can't drift from each other.
/// </summary>
public static class SchedulingPersistenceServiceCollectionExtensions
{
    public static IServiceCollection AddSchedulingPersistence(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<SchedulingDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql =>
                    npgsql.MigrationsHistoryTable("__ef_migrations_history", "scheduling"))
                .AddInterceptors(new AuditCompletenessInterceptor()));

        services.AddScoped<ISchedulingData, SchedulingData>();
        services.AddKeyedScoped<IAuditWriter, AuditWriter<SchedulingDbContext>>(AuditWriterKeys.Scheduling);

        return services;
    }
}
