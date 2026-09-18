using Microsoft.EntityFrameworkCore;
using Mise.Modules.Tables.Domain;
using Mise.Modules.Tables.Infrastructure.Persistence.Configurations;
using Mise.SharedKernel.Infrastructure;
using Mise.SharedKernel.Persistence;

namespace Mise.Modules.Tables.Infrastructure.Persistence;

public sealed class TablesDbContext(DbContextOptions<TablesDbContext> options) : DbContext(options)
{
    internal DbSet<Section> Sections => Set<Section>();
    internal DbSet<Table> Tables => Set<Table>();
    internal DbSet<TableGroup> TableGroups => Set<TableGroup>();
    internal DbSet<ProcessedOperation> ProcessedOperations => Set<ProcessedOperation>();
    internal DbSet<AuditLogEntry> AuditLogEntries => Set<AuditLogEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("tables");
        modelBuilder.ApplyConfiguration(new SectionConfiguration());
        modelBuilder.ApplyConfiguration(new TableConfiguration());
        modelBuilder.ApplyConfiguration(new TableGroupConfiguration());
        // isOwner: false — Reservations' migration owns shared.processed_operation/
        // audit_log_entry's DDL (it was first to need them, Phase 2). Tables only reads/writes
        // the physical tables Reservations' migration creates, same as StaffIdentity (Phase 3).
        // Mise.MigrationService must migrate Reservations before Tables.
        modelBuilder.ApplySharedKernelConfigurations(isOwner: false);
    }
}
