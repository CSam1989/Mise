using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Mise.Modules.StaffIdentity.Domain;
using Mise.Modules.StaffIdentity.Infrastructure.Persistence.Configurations;
using Mise.SharedKernel.Infrastructure;
using Mise.SharedKernel.Persistence;

namespace Mise.Modules.StaffIdentity.Infrastructure.Persistence;

/// <summary>
/// IdentityUserContext (not IdentityDbContext) — no Roles table, deliberately: the charter
/// models Role as a single required enum column on StaffUser, not a many-to-many Identity
/// role assignment (there are exactly two fixed roles for v1). AspNetUsers/UserClaims/
/// UserLogins/UserTokens keep Identity's own default (PascalCase) table names — standard
/// practice, not remapped to this project's snake_case convention, since they're
/// framework-owned rather than part of this project's own schema design.
/// </summary>
public sealed class StaffIdentityDbContext(DbContextOptions<StaffIdentityDbContext> options)
    : IdentityUserContext<StaffIdentityUser, Guid>(options)
{
    internal DbSet<StaffUser> StaffUsers => Set<StaffUser>();
    internal DbSet<ProcessedOperation> ProcessedOperations => Set<ProcessedOperation>();
    internal DbSet<AuditLogEntry> AuditLogEntries => Set<AuditLogEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("staff_identity");

        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfiguration(new StaffUserConfiguration());
        // processed_operation and audit_log_entry declare their own "shared" schema
        // explicitly, overriding the default above. isOwner: false — Reservations' migration
        // creates these tables; this DbContext only reads/writes them (its own migration
        // excludes them, or it would try to re-create tables that already exist). This means
        // Mise.MigrationService must migrate Reservations before StaffIdentity.
        modelBuilder.ApplySharedKernelConfigurations(isOwner: false);
    }
}
