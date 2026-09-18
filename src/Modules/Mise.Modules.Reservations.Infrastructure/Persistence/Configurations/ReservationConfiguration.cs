using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Mise.Modules.Reservations.Domain;

namespace Mise.Modules.Reservations.Infrastructure.Persistence.Configurations;

/// <summary>
/// Phase 6 grows this past Phase 2's four-column shape. No physical FK on <c>table_id</c>
/// (Tables module) or <c>created_by_staff_id</c> (StaffIdentity module): each module owns its
/// own schema, and a cross-module FK would tie migration ordering and DDL ownership across
/// module boundaries the way CLAUDE.md's boundary table already forbids for code references —
/// existence is checked via <c>ITableAvailabilityLookup</c> (Tables.Contracts) in Application
/// instead (<see cref="TableAssignmentGuard"/>). <c>customer_name</c>'s GIN trigram index and
/// BR-01's exclusion constraint are hand-written raw SQL in the migration itself (no EF Core
/// fluent API for either) — see that migration's own comments.
/// </summary>
internal sealed class ReservationConfiguration : IEntityTypeConfiguration<Reservation>
{
    public void Configure(EntityTypeBuilder<Reservation> builder)
    {
        builder.ToTable("reservation");
        builder.HasKey(r => r.Id);

        builder.Property(r => r.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(r => r.CustomerName).HasColumnName("customer_name").HasMaxLength(200).IsRequired();
        builder.Property(r => r.CustomerPhone).HasColumnName("customer_phone").HasMaxLength(20).IsRequired();
        builder.Property(r => r.CustomerEmail).HasColumnName("customer_email").HasMaxLength(200);
        builder.Property(r => r.PartySize).HasColumnName("party_size").IsRequired();
        builder.Property(r => r.ReservationDateTime)
            .HasColumnName("reservation_date_time")
            .HasColumnType("timestamptz")
            .IsRequired();
        builder.Property(r => r.DurationMinutes).HasColumnName("duration_minutes").IsRequired();
        builder.Property(r => r.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(r => r.TableId).HasColumnName("table_id");
        builder.Property(r => r.Notes).HasColumnName("notes").HasMaxLength(500);
        builder.Property(r => r.CreatedByStaffId).HasColumnName("created_by_staff_id").IsRequired();
        builder.Property(r => r.CreatedAtUtc).HasColumnName("created_at_utc").HasColumnType("timestamptz").IsRequired();
        builder.Property(r => r.UpdatedAtUtc).HasColumnName("updated_at_utc").HasColumnType("timestamptz").IsRequired();

        // Charter §10 Key Indexes: ReservationDateTime (day/time range queries) and TableId
        // (table history lookups). CustomerPhone's plain B-tree also comes from that list —
        // the trigram index over CustomerName is added via raw SQL in the migration instead,
        // since HasMethod("gin")/HasOperators("gin_trgm_ops") needs pg_trgm to exist first.
        builder.HasIndex(r => r.ReservationDateTime);
        builder.HasIndex(r => r.TableId);
        builder.HasIndex(r => r.CustomerPhone);

        // docs/plan.md correction #5, extended to Reservation now that Update/Cancel give xmin
        // something to protect (Phase 2's create-only skeleton had nothing to protect yet) —
        // same IsRowVersion()-to-xmin mapping as TableConfiguration; see that file's doc comment
        // for the mechanism.
        builder.Property<uint>("Version").IsRowVersion();

        builder.Ignore(r => r.DomainEvents);
    }
}
