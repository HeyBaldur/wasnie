using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wasnie.Domain.Compensation.Assignments;

namespace Wasnie.Infrastructure.Persistence.Configurations.Compensation;

public sealed class PlanAssignmentConfiguration : IEntityTypeConfiguration<PlanAssignment>
{
    public void Configure(EntityTypeBuilder<PlanAssignment> builder)
    {
        builder.ToTable("PlanAssignments");

        builder.HasKey(a => a.Id);

        builder.Property(a => a.TenantId).IsRequired();
        builder.Property(a => a.PlanId).IsRequired();
        builder.Property(a => a.PayeeId).IsRequired();
        builder.Property(a => a.Status).HasConversion<string>().HasMaxLength(50).IsRequired();
        builder.Property(a => a.CreatedAt).IsRequired();
        builder.Property(a => a.CreatedBy).IsRequired().HasMaxLength(450);
        builder.Property(a => a.UpdatedAt).IsRequired();
        builder.Property(a => a.UpdatedBy).IsRequired().HasMaxLength(450);

        builder.OwnsOne(a => a.PayeeSnapshot, ps =>
        {
            ps.Property(x => x.PayeeId).HasColumnName("SnapshotPayeeId").IsRequired();
            ps.Property(x => x.FullName).HasColumnName("SnapshotFullName").HasMaxLength(201).IsRequired();
            ps.Property(x => x.EmployeeCode).HasColumnName("SnapshotEmployeeCode").HasMaxLength(50).IsRequired();
        });

        builder.OwnsOne(a => a.EffectivePeriod, dr =>
        {
            dr.Property(d => d.Start).HasColumnName("EffectiveStart").IsRequired();
            dr.Property(d => d.End).HasColumnName("EffectiveEnd").IsRequired();
        });

        builder.HasIndex(a => new { a.TenantId, a.PlanId, a.PayeeId });
    }
}
