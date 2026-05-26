using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wasnie.Domain.Compensation.Quotas;

namespace Wasnie.Infrastructure.Persistence.Configurations.Compensation;

public sealed class QuotaConfiguration : IEntityTypeConfiguration<Quota>
{
    public void Configure(EntityTypeBuilder<Quota> builder)
    {
        builder.ToTable("Quotas");

        builder.HasKey(q => q.Id);

        builder.Property(q => q.TenantId).IsRequired();
        builder.Property(q => q.PayeeId).IsRequired();
        builder.Property(q => q.PlanId).IsRequired();
        builder.Property(q => q.Status).HasConversion<string>().HasMaxLength(50).IsRequired();
        builder.Property(q => q.CreatedAt).IsRequired();
        builder.Property(q => q.CreatedBy).IsRequired().HasMaxLength(450);
        builder.Property(q => q.UpdatedAt).IsRequired();
        builder.Property(q => q.UpdatedBy).IsRequired().HasMaxLength(450);

        builder.OwnsOne(q => q.Amount, m =>
        {
            m.Property(x => x.Amount).HasColumnName("QuotaAmount").HasColumnType("decimal(18,4)").IsRequired();
            m.Property(x => x.Currency).HasColumnName("QuotaCurrency").HasMaxLength(3).IsRequired();
        });

        builder.OwnsOne(q => q.Period, dr =>
        {
            dr.Property(d => d.Start).HasColumnName("PeriodStart").IsRequired();
            dr.Property(d => d.End).HasColumnName("PeriodEnd").IsRequired();
        });

        builder.Property(q => q.MeasurementType).HasConversion<int>().IsRequired();
        builder.Property(q => q.Notes).HasMaxLength(500);

        builder.HasIndex(q => new { q.TenantId, q.PayeeId, q.PlanId });
        builder.HasIndex(q => new { q.TenantId, q.Status });
        builder.HasIndex(q => new { q.TenantId, q.PayeeId });
    }
}
