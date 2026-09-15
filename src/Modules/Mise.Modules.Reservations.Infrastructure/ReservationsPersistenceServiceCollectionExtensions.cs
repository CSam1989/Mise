using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Mise.Modules.Reservations.Application.Ports;
using Mise.Modules.Reservations.Infrastructure.Persistence;
using Mise.SharedKernel.Infrastructure;

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
                npgsql.MigrationsHistoryTable("__ef_migrations_history", "reservations")));

        services.AddScoped<IReservationsData, ReservationsData>();
        services.AddScoped<IAuditWriter, AuditWriter>();

        return services;
    }
}
