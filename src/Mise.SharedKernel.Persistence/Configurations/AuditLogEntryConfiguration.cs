using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Mise.SharedKernel.Infrastructure;

namespace Mise.SharedKernel.Persistence.Configurations;

/// <param name="excludeFromMigrations">True for every module except the one owning this
/// table's DDL (see SharedKernelModelBuilderExtensions) — a non-owning module still maps and
/// queries the same physical table, it just doesn't try to (re-)create it.</param>
public sealed class AuditLogEntryConfiguration(bool excludeFromMigrations = false) : IEntityTypeConfiguration<AuditLogEntry>
{
    public void Configure(EntityTypeBuilder<AuditLogEntry> builder)
    {
        builder.ToTable("audit_log_entry", "shared", t =>
        {
            if (excludeFromMigrations)
            {
                t.ExcludeFromMigrations();
            }
        });
        builder.HasKey(a => a.Id);

        builder.Property(a => a.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(a => a.EntityType).HasColumnName("entity_type").HasMaxLength(100).IsRequired();
        builder.Property(a => a.EntityId).HasColumnName("entity_id").IsRequired();
        builder.Property(a => a.Action).HasColumnName("action").HasMaxLength(100).IsRequired();
        builder.Property(a => a.PerformedByStaffId).HasColumnName("performed_by_staff_id");
        builder.Property(a => a.PerformedBySystemProcess).HasColumnName("performed_by_system_process").HasMaxLength(200);
        builder.Property(a => a.OccurredAtUtc).HasColumnName("occurred_at_utc").HasColumnType("timestamptz").IsRequired();
        builder.Property(a => a.Details).HasColumnName("details").IsRequired();

        // Phase 9 (ADR-009) — supports IAuditReader.GetHistoryAsync's WHERE entity_type = ... AND
        // entity_id = ... lookup (the new audit-history endpoints); previously only the PK index
        // existed, making that query an unindexed scan.
        builder.HasIndex(a => new { a.EntityType, a.EntityId }).HasDatabaseName("ix_audit_log_entry_entity_type_entity_id");
    }
}
