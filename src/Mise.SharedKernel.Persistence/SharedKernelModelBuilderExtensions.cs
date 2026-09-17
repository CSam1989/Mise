using Microsoft.EntityFrameworkCore;
using Mise.SharedKernel.Persistence.Configurations;

namespace Mise.SharedKernel.Persistence;

/// <summary>
/// Applied by every module DbContext that needs OperationId idempotency and/or audit
/// writing — today Reservations and StaffIdentity. Both tables declare their own "shared"
/// schema explicitly (see the configurations), overriding whatever default schema the
/// calling DbContext set for its own aggregate tables.
/// </summary>
public static class SharedKernelModelBuilderExtensions
{
    /// <param name="isOwner">
    /// Exactly one module's migration may actually create shared.processed_operation and
    /// shared.audit_log_entry — every module maps the same physical tables (so any module can
    /// read/write them), but only the owner's migration contains the CreateTable calls
    /// (EF's ExcludeFromMigrations on every non-owner). Reservations was first to need them
    /// (Phase 2) and stays the owner; StaffIdentity (Phase 3, the second consumer that forced
    /// this extraction — see this project's own doc comment) passes isOwner: false.
    /// Consequence: Mise.MigrationService must run the owner's migration before any
    /// non-owner's, or a non-owner's queries would hit tables that don't exist yet.
    /// </param>
    public static ModelBuilder ApplySharedKernelConfigurations(this ModelBuilder modelBuilder, bool isOwner = true)
    {
        modelBuilder.ApplyConfiguration(new ProcessedOperationConfiguration(excludeFromMigrations: !isOwner));
        modelBuilder.ApplyConfiguration(new AuditLogEntryConfiguration(excludeFromMigrations: !isOwner));
        return modelBuilder;
    }
}
