using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Mise.Modules.Tables.Domain;

namespace Mise.Modules.Tables.Infrastructure.Persistence.Configurations;

internal sealed class TableConfiguration : IEntityTypeConfiguration<Table>
{
    public void Configure(EntityTypeBuilder<Table> builder)
    {
        builder.ToTable("table");
        builder.HasKey(t => t.Id);

        builder.Property(t => t.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(t => t.SectionId).HasColumnName("section_id").IsRequired();
        builder.Property(t => t.Name).HasColumnName("name").HasMaxLength(50).IsRequired();
        builder.Property(t => t.MinCapacity).HasColumnName("min_capacity").IsRequired();
        builder.Property(t => t.MaxCapacity).HasColumnName("max_capacity").IsRequired();
        builder.Property(t => t.IsCombinable).HasColumnName("is_combinable").IsRequired();
        builder.Property(t => t.PositionX).HasColumnName("position_x");
        builder.Property(t => t.PositionY).HasColumnName("position_y");
        builder.Property(t => t.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(t => t.IsActive).HasColumnName("is_active").IsRequired();

        // Charter §10: "Unique constraint: (SectionId, Name) on Table."
        builder.HasIndex(t => new { t.SectionId, t.Name }).IsUnique();

        // Charter §10: "SectionId | uuid | NO | FK → Section." No CLR navigation property on
        // either side (Section and Table are deliberately independent aggregates — the
        // "testable seam" gateways never need to load one through the other), so this uses
        // EF's no-navigation relationship API instead of the usual HasOne(nav).WithMany(nav).
        // Restrict, not the EF default Cascade: a Section is soft-deactivated in practice
        // (IsActive), never hard-deleted, but if that ever changed, silently cascading away
        // every table in it would be exactly the kind of surprise charter correction #11
        // (ConflictRecord's FKs) already flags as unacceptable for this project.
        builder.HasOne<Section>().WithMany().HasForeignKey(t => t.SectionId).OnDelete(DeleteBehavior.Restrict);

        // docs/plan.md correction #5 — Postgres's built-in xmin system column as the
        // optimistic-concurrency token, no extra stored column needed (ADR-001 Amendment 1).
        // Npgsql.EntityFrameworkCore.PostgreSQL 10.0.3 has no UseXminAsConcurrencyToken()
        // helper (removed/renamed since the charter's ADR-001 Amendment 1 was written against
        // an older version) — instead its own model-finalizing convention
        // (NpgsqlPostgresModelFinalizingConvention.ProcessRowVersionProperty) auto-detects any
        // uint shadow property marked IsRowVersion() and silently maps it to the physical
        // "xmin" system column regardless of the shadow property's own name. The gateway
        // (TablesData) is what actually reads/writes it via that shadow property name
        // ("Version"); Application/Domain only ever see it as an opaque uint.
        builder.Property<uint>("Version").IsRowVersion();

        builder.Ignore(t => t.DomainEvents);
    }
}
