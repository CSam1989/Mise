using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Mise.SharedKernel.Persistence.Configurations;

/// <param name="excludeFromMigrations">True for every module except the one owning this
/// table's DDL (see SharedKernelModelBuilderExtensions) — a non-owning module still maps and
/// queries the same physical table, it just doesn't try to (re-)create it.</param>
public sealed class ProcessedOperationConfiguration(bool excludeFromMigrations = false) : IEntityTypeConfiguration<ProcessedOperation>
{
    public void Configure(EntityTypeBuilder<ProcessedOperation> builder)
    {
        builder.ToTable("processed_operation", "shared", t =>
        {
            if (excludeFromMigrations)
            {
                t.ExcludeFromMigrations();
            }
        });
        builder.HasKey(p => p.OperationId);

        builder.Property(p => p.OperationId).HasColumnName("operation_id").ValueGeneratedNever();
        builder.Property(p => p.ResourceType).HasColumnName("resource_type").HasMaxLength(100).IsRequired();
        builder.Property(p => p.ResourceId).HasColumnName("resource_id").IsRequired();
        builder.Property(p => p.ProcessedAtUtc).HasColumnName("processed_at_utc").HasColumnType("timestamptz").IsRequired();
    }
}
