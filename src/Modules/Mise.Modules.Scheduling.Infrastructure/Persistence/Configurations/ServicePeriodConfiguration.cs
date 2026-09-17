using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Mise.Modules.Scheduling.Domain;

namespace Mise.Modules.Scheduling.Infrastructure.Persistence.Configurations;

internal sealed class ServicePeriodConfiguration : IEntityTypeConfiguration<ServicePeriod>
{
    public void Configure(EntityTypeBuilder<ServicePeriod> builder)
    {
        builder.ToTable("service_period");
        builder.HasKey(s => s.Id);

        builder.Property(s => s.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(s => s.Date).HasColumnName("date").IsRequired();
        builder.Property(s => s.Label).HasColumnName("label").HasMaxLength(30).IsRequired();
        builder.Property(s => s.StartTime).HasColumnName("start_time").IsRequired();
        builder.Property(s => s.EndTime).HasColumnName("end_time").IsRequired();
        builder.Property(s => s.EndsNextDay).HasColumnName("ends_next_day").IsRequired();
        builder.Property(s => s.IsClosed).HasColumnName("is_closed").IsRequired();

        // No xmin concurrency token — same scope decision as Section (docs/plan.md correction
        // #5 names only Table/Reservation as concurrency-tracked): schedule edits are
        // infrequent, low-conflict Manager-config changes, last-write-wins for now.
        builder.Ignore(s => s.DomainEvents);
    }
}
