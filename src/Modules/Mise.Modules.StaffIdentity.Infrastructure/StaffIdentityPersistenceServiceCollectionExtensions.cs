using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Mise.Modules.StaffIdentity.Application.Ports;
using Mise.Modules.StaffIdentity.Infrastructure.Persistence;
using Mise.Modules.StaffIdentity.Infrastructure.Security;
using Mise.SharedKernel.Infrastructure;
using Mise.SharedKernel.Persistence;

namespace Mise.Modules.StaffIdentity.Infrastructure;

/// <summary>
/// Single place the module's DbContext options (including the per-module migrations-history
/// table) are configured, mirroring ReservationsPersistenceServiceCollectionExtensions —
/// Mise.ApiService and Mise.MigrationService can't drift from each other.
/// </summary>
public static class StaffIdentityPersistenceServiceCollectionExtensions
{
    public static IServiceCollection AddStaffIdentityPersistence(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<StaffIdentityDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql =>
                    npgsql.MigrationsHistoryTable("__ef_migrations_history", "staff_identity"))
                .AddInterceptors(new AuditCompletenessInterceptor()));

        services.AddIdentityCore<StaffIdentityUser>(options =>
            {
                // RegisterStaffCommandValidator (FluentValidation) is this project's single
                // source of password policy (CLAUDE.md's "exact message string" rule) — these
                // are relaxed to match it exactly rather than left at Identity's own stricter
                // defaults, which would otherwise reject something FluentValidation already
                // accepted with a confusing, un-field-scoped IdentityResult error instead of
                // the validator's own message.
                options.Password.RequireDigit = false;
                options.Password.RequireLowercase = false;
                options.Password.RequireUppercase = false;
                options.Password.RequireNonAlphanumeric = false;
                options.Password.RequiredLength = 8;
                options.User.RequireUniqueEmail = false;
            })
            .AddEntityFrameworkStores<StaffIdentityDbContext>();

        services.AddScoped<IStaffIdentityData, StaffIdentityData>();
        services.AddKeyedScoped<IAuditWriter, AuditWriter<StaffIdentityDbContext>>(AuditWriterKeys.StaffIdentity);
        services.AddScoped<StaffIdentitySeeder>();

        return services;
    }

    /// <summary>
    /// Separate from <see cref="AddStaffIdentityPersistence"/> because Mise.MigrationService
    /// needs the DbContext/Identity/seeder registrations above but never mints a token — only
    /// Mise.ApiService (which actually serves the login endpoint) calls this one too.
    /// </summary>
    public static IServiceCollection AddStaffIdentityJwtIssuer(this IServiceCollection services, string jwtSigningKey)
    {
        services.AddSingleton<IJwtTokenIssuer>(sp =>
            new JwtTokenIssuer(jwtSigningKey, sp.GetRequiredService<TimeProvider>()));

        return services;
    }
}
