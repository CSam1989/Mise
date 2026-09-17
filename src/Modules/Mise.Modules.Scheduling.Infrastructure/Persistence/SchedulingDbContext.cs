using Microsoft.EntityFrameworkCore;
using Mise.Modules.Scheduling.Domain;
using Mise.Modules.Scheduling.Infrastructure.Persistence.Configurations;
using Mise.SharedKernel.Infrastructure;
using Mise.SharedKernel.Persistence;

namespace Mise.Modules.Scheduling.Infrastructure.Persistence;

public sealed class SchedulingDbContext(DbContextOptions<SchedulingDbContext> options) : DbContext(options)
{
    internal DbSet<ServicePeriod> ServicePeriods => Set<ServicePeriod>();
    internal DbSet<ProcessedOperation> ProcessedOperations => Set<ProcessedOperation>();
    internal DbSet<AuditLogEntry> AuditLogEntries => Set<AuditLogEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("scheduling");
        modelBuilder.ApplyConfiguration(new ServicePeriodConfiguration());
        // isOwner: false — Reservations' migration owns shared.processed_operation/
        // audit_log_entry's DDL (Phase 2, first to need them). Scheduling only reads/writes
        // the physical tables Reservations' migration creates, same as StaffIdentity and
        // Tables. Mise.MigrationService must migrate Reservations before Scheduling.
        modelBuilder.ApplySharedKernelConfigurations(isOwner: false);
    }
}
