using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wasnie.Domain.Compensation.Plans;

namespace Wasnie.Infrastructure.Persistence.Configurations.Compensation;

public sealed class CompensationPlanConfiguration : IEntityTypeConfiguration<Plan>
{
    public void Configure(EntityTypeBuilder<Plan> builder)
    {
        builder.ToTable("CompensationPlans");

        builder.HasKey(p => p.Id);

        builder.Property(p => p.TenantId).IsRequired();
        builder.Property(p => p.Name).IsRequired().HasMaxLength(200);
        builder.Property(p => p.Description).HasMaxLength(1000);
        builder.Property(p => p.Version).IsRequired();
        builder.Property(p => p.Status).HasConversion<string>().HasMaxLength(50).IsRequired();
        builder.Property(p => p.PeriodType).HasConversion<string>().HasMaxLength(50).IsRequired(false);
        builder.Property(p => p.Currency).IsRequired().HasMaxLength(3);
        builder.Property(p => p.CreatedAt).IsRequired();
        builder.Property(p => p.CreatedBy).IsRequired().HasMaxLength(450);
        builder.Property(p => p.UpdatedAt).IsRequired();
        builder.Property(p => p.UpdatedBy).IsRequired().HasMaxLength(450);

        builder.OwnsOne(p => p.EffectivePeriod, dr =>
        {
            dr.Property(d => d.Start).HasColumnName("EffectiveStart").IsRequired();
            dr.Property(d => d.End).HasColumnName("EffectiveEnd").IsRequired();
        });

        builder.HasMany(p => p.Rules)
            .WithOne()
            .HasForeignKey(r => r.PlanId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(p => new { p.TenantId, p.Name });
        builder.HasIndex(p => new { p.TenantId, p.Status });
    }
}
