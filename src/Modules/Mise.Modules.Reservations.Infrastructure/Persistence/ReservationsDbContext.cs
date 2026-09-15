using Microsoft.EntityFrameworkCore;
using Mise.Modules.Reservations.Domain;
using Mise.Modules.Reservations.Infrastructure.Persistence.Configurations;
using Mise.SharedKernel.Infrastructure;

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
        // explicitly, overriding the default above.
        modelBuilder.ApplyConfiguration(new ProcessedOperationConfiguration());
        modelBuilder.ApplyConfiguration(new AuditLogEntryConfiguration());
    }
}
