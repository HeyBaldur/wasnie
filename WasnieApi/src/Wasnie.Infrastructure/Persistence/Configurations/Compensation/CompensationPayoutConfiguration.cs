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
        builder.Property(p => p.PayRunId).IsRequired(false);
        builder.Property(p => p.PayeeId).IsRequired();
        builder.Property(p => p.PlanId).IsRequired();
        builder.Property(p => p.Status).HasConversion<string>().HasMaxLength(50).IsRequired();
        // Nullable by design: only a Paid payout has a payment date (see the invariant on the property).
        builder.Property(p => p.PaidAt).IsRequired(false);
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

        // FK to PayRun — nullable (orphan payouts have PayRunId = NULL).
        // Restrict: a PayRun with linked payouts cannot be deleted — paid runs are
        // immutable history and severing the payout↔run link is an audit hole.
        builder.HasOne<PayRun>()
            .WithMany()
            .HasForeignKey(p => p.PayRunId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(p => p.DiscardedAt);
        builder.Property(p => p.DiscardedBy).HasMaxLength(450);
        // Long enough for a real explanation: this is the sentence an auditor reads to understand why
        // a payout worth thousands stopped being owed.
        builder.Property(p => p.DiscardReason).HasMaxLength(2000);

        builder.HasIndex(p => new { p.TenantId, p.PayeeId });

        // Serves the cash-flow reporting predicate (tenant + Status == Paid + PaidAt in range) used by
        // the dashboard payouts widget and the payouts list when filtered by payment date.
        builder.HasIndex(p => new { p.TenantId, p.Status, p.PaidAt })
            .HasDatabaseName("IX_CompensationPayouts_Tenant_Status_PaidAt");

        // IX_CompensationPayouts_Live: unique filtered index.
        // Since A4 it covered (TenantId, PayeeId, PlanId, PeriodStart, PeriodEnd) WHERE Status not Paid/Disputed.
        // Since A6 it covers (TenantId, PayRunId, PayeeId, PlanId) WHERE Status not Paid/Disputed AND PayRunId IS NOT NULL.
        // PeriodStart/PeriodEnd are owned-type columns; the new index uses direct columns only, so it
        // could be expressed via HasIndex — but it is kept as raw SQL in migration A6_AddPayRun to be
        // consistent with the A4 pattern and to keep the filter clause explicit and reviewable.
    }
}
