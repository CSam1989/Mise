using Microsoft.EntityFrameworkCore;
using Mise.Modules.Reservations.Domain;
using Mise.Modules.Reservations.Infrastructure.Persistence.Configurations;
using Mise.SharedKernel.Infrastructure;
using Mise.SharedKernel.Persistence;

namespace Mise.Modules.Reservations.Infrastructure.Persistence;

public sealed class ReservationsDbContext(DbContextOptions<ReservationsDbContext> options) : DbContext(options)
{
    internal DbSet<Reservation> Reservations => Set<Reservation>();
    internal DbSet<ProcessedOperation> ProcessedOperations => Set<ProcessedOperation>();
    internal DbSet<AuditLogEntry> AuditLogEntries => Set<AuditLogEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("reservations");
        modelBuilder.ApplyConfiguration(new ReservationConfiguration());
        // processed_operation and audit_log_entry declare their own "shared" schema
        // explicitly, overriding the default above. Shared with StaffIdentity's DbContext
        // (Mise.SharedKernel.Persistence, Phase 3) — see that project's doc comment for why
        // this couldn't live in Mise.SharedKernel.Infrastructure instead.
        modelBuilder.ApplySharedKernelConfigurations();
    }
}
