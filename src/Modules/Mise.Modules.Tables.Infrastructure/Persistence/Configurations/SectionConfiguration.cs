using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Mise.Modules.Tables.Domain;

namespace Mise.Modules.Tables.Infrastructure.Persistence.Configurations;

internal sealed class SectionConfiguration : IEntityTypeConfiguration<Section>
{
    public void Configure(EntityTypeBuilder<Section> builder)
    {
        builder.ToTable("section");
        builder.HasKey(s => s.Id);

        builder.Property(s => s.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(s => s.Name).HasColumnName("name").HasMaxLength(50).IsRequired();
        builder.Property(s => s.DisplayOrder).HasColumnName("display_order").IsRequired();
        builder.Property(s => s.IsActive).HasColumnName("is_active").IsRequired();

        // No xmin concurrency token: unlike Table, Section isn't named as a concurrency-tracked
        // aggregate in docs/plan.md correction #5 — edits are lower-frequency and lower-conflict
        // than floor-plan/table edits, so this stays last-write-wins for now (a scope decision,
        // not an oversight; revisit if NFR-03's concurrency concern turns out to bite here too).
        builder.Ignore(s => s.DomainEvents);
    }
}
