using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Mise.Modules.StaffIdentity.Domain;

namespace Mise.Modules.StaffIdentity.Infrastructure.Persistence.Configurations;

internal sealed class StaffUserConfiguration : IEntityTypeConfiguration<StaffUser>
{
    public void Configure(EntityTypeBuilder<StaffUser> builder)
    {
        builder.ToTable("staff_user");
        builder.HasKey(s => s.Id);

        builder.Property(s => s.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(s => s.FullName).HasColumnName("full_name").HasMaxLength(100).IsRequired();
        builder.Property(s => s.Role).HasColumnName("role").HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(s => s.IsActive).HasColumnName("is_active").IsRequired();

        // AggregateRoot<TId>.DomainEvents is a computed, get-only collection — not a column
        // and not a navigation; EF Core has nothing sensible to do with it by convention.
        builder.Ignore(s => s.DomainEvents);
    }
}
