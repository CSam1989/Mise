using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Mise.Modules.Reservations.Domain;

namespace Mise.Modules.Reservations.Infrastructure.Persistence.Configurations;

internal sealed class ReservationConfiguration : IEntityTypeConfiguration<Reservation>
{
    public void Configure(EntityTypeBuilder<Reservation> builder)
    {
        builder.ToTable("reservation");
        builder.HasKey(r => r.Id);

        builder.Property(r => r.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(r => r.CustomerName).HasColumnName("customer_name").HasMaxLength(200).IsRequired();
        builder.Property(r => r.PartySize).HasColumnName("party_size").IsRequired();
        builder.Property(r => r.ReservationDateTime)
            .HasColumnName("reservation_date_time")
            .HasColumnType("timestamptz")
            .IsRequired();
        builder.Property(r => r.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(20).IsRequired();

        // AggregateRoot<TId>.DomainEvents is a computed, get-only collection — not a column
        // and not a navigation; EF Core has nothing sensible to do with it by convention.
        builder.Ignore(r => r.DomainEvents);
    }
}
