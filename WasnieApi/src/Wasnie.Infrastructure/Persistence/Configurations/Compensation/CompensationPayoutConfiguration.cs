using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wasnie.Domain.Compensation.Payouts;

namespace Wasnie.Infrastructure.Persistence.Configurations.Compensation;

public sealed class CompensationPayoutConfiguration : IEntityTypeConfiguration<CompensationPayout>
{
    public void Configure(EntityTypeBuilder<CompensationPayout> builder)
    {
        builder.ToTable("CompensationPayouts");

        builder.HasKey(p => p.Id);

        builder.Property(p => p.TenantId).IsRequired();
        builder.Property(p => p.PayeeId).IsRequired();
        builder.Property(p => p.PlanId).IsRequired();
        builder.Property(p => p.Status).HasConversion<string>().HasMaxLength(50).IsRequired();
        builder.Property(p => p.CalculatedAt).IsRequired();
        builder.Property(p => p.CalculatedBy).IsRequired().HasMaxLength(450);
        builder.Property(p => p.UpdatedAt).IsRequired();
        builder.Property(p => p.UpdatedBy).IsRequired().HasMaxLength(450);

        builder.OwnsOne(p => p.TotalCommission, m =>
        {
            m.Property(x => x.Amount).HasColumnName("TotalCommissionAmount").HasColumnType("decimal(18,4)").IsRequired();
            m.Property(x => x.Currency).HasColumnName("TotalCommissionCurrency").HasMaxLength(3).IsRequired();
        });

        builder.OwnsOne(p => p.PayeeSnapshot, ps =>
        {
            ps.Property(x => x.PayeeId).HasColumnName("SnapshotPayeeId").IsRequired();
            ps.Property(x => x.FullName).HasColumnName("SnapshotFullName").HasMaxLength(201).IsRequired();
            ps.Property(x => x.EmployeeCode).HasColumnName("SnapshotEmployeeCode").HasMaxLength(50).IsRequired();
        });

        builder.OwnsOne(p => p.Period, dr =>
        {
            dr.Property(d => d.Start).HasColumnName("PeriodStart").IsRequired();
            dr.Property(d => d.End).HasColumnName("PeriodEnd").IsRequired();
        });

        builder.HasMany(p => p.Lines)
            .WithOne()
            .HasForeignKey(l => l.PayoutId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(p => new { p.TenantId, p.PayeeId });

        // IX_CompensationPayouts_Live is a unique filtered index on
        // (TenantId, PayeeId, PlanId, PeriodStart, PeriodEnd) WHERE Status NOT IN ('Paid','Disputed').
        // It cannot be expressed via HasIndex because PeriodStart/PeriodEnd are owned-type columns.
        // The index is created via raw SQL in migration A4_AddPayoutPlanId.
    }
}
