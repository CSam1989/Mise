using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Mise.Modules.Reservations.Application.Ports;
using Mise.Modules.Reservations.Contracts;
using Mise.Modules.Reservations.Infrastructure.Persistence;
using Mise.SharedKernel.Infrastructure;
using Mise.SharedKernel.Persistence;

namespace Mise.Modules.Reservations.Infrastructure;

/// <summary>
/// Single place the module's DbContext options (including the per-module migrations-history
/// table — plan.md's "four DbContexts sharing one Postgres database" gotcha) are configured,
/// so Mise.ApiService and Mise.MigrationService can't drift from each other.
/// </summary>
public static class ReservationsPersistenceServiceCollectionExtensions
{
    public static IServiceCollection AddReservationsPersistence(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<ReservationsDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql =>
                    npgsql.MigrationsHistoryTable("__ef_migrations_history", "reservations"))
                .AddInterceptors(new AuditCompletenessInterceptor()));

        services.AddScoped<IReservationsData, ReservationsData>();
        services.AddScoped<IReservationLookup, ReservationLookup>();
        services.AddKeyedScoped<IAuditWriter, AuditWriter<ReservationsDbContext>>(AuditWriterKeys.Reservations);

        return services;
    }
}
