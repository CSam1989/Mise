using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Mise.Modules.Reservations.Infrastructure.Persistence.Configurations;

internal sealed class ProcessedOperationConfiguration : IEntityTypeConfiguration<ProcessedOperation>
{
    public void Configure(EntityTypeBuilder<ProcessedOperation> builder)
    {
        builder.ToTable("processed_operation", "shared");
        builder.HasKey(p => p.OperationId);

        builder.Property(p => p.OperationId).HasColumnName("operation_id").ValueGeneratedNever();
        builder.Property(p => p.ResourceType).HasColumnName("resource_type").HasMaxLength(100).IsRequired();
        builder.Property(p => p.ResourceId).HasColumnName("resource_id").IsRequired();
        builder.Property(p => p.ProcessedAtUtc).HasColumnName("processed_at_utc").HasColumnType("timestamptz").IsRequired();
    }
}
