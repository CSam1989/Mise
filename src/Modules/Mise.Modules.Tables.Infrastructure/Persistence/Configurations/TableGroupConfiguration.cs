using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Mise.Modules.Tables.Domain;

namespace Mise.Modules.Tables.Infrastructure.Persistence.Configurations;

/// <summary>
/// <see cref="TableGroup.TableIds"/> maps to a native Postgres <c>uuid[]</c> column (EF Core's
/// primitive-collection support) rather than a separate join table — CLAUDE.md's Reservations
/// Phase 6 section / ADR-006 picked this over a join table deliberately: a handful of rows for
/// a single restaurant's floor plan never needs relational querying of its own, and the "is
/// this table already grouped" check (CreateTableGroupCommandHandler) reads every active
/// group's array back into memory rather than querying the array column directly, so there is
/// no query shape that would benefit from a join table's indexability. No xmin — same
/// no-concurrency scope decision as Section (Create-only this phase, no Update/Deactivate to
/// protect yet).
/// </summary>
internal sealed class TableGroupConfiguration : IEntityTypeConfiguration<TableGroup>
{
    public void Configure(EntityTypeBuilder<TableGroup> builder)
    {
        builder.ToTable("table_group");
        builder.HasKey(g => g.Id);

        builder.Property(g => g.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(g => g.Name).HasColumnName("name").HasMaxLength(50).IsRequired();
        builder.Property(g => g.IsActive).HasColumnName("is_active").IsRequired();

        // Read-only IReadOnlyList<Guid> property backed by the private _tableIds field — same
        // "no setter, EF goes through the field" shape as AggregateRoot<TId>.DomainEvents
        // elsewhere in this codebase, made explicit here since this one IS mapped (not Ignored).
        builder.Property(g => g.TableIds).HasColumnName("table_ids").IsRequired()
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.Ignore(g => g.DomainEvents);
    }
}
