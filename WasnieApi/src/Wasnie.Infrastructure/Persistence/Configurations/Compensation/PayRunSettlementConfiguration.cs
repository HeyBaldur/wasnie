using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wasnie.Domain.Compensation.Ledger;

namespace Wasnie.Infrastructure.Persistence.Configurations.Compensation;

public sealed class PayRunSettlementConfiguration : IEntityTypeConfiguration<PayRunSettlement>
{
    public void Configure(EntityTypeBuilder<PayRunSettlement> builder)
    {
        builder.ToTable("PayRunSettlements");

        builder.HasKey(s => s.Id);

        builder.Property(s => s.TenantId).IsRequired();
        builder.Property(s => s.PayRunId).IsRequired();
        builder.Property(s => s.PayeeId).IsRequired();
        builder.Property(s => s.Currency).IsRequired().HasMaxLength(3);
        builder.Property(s => s.LedgerEntryId).IsRequired(false);
        builder.Property(s => s.AppliedAt).IsRequired();
        builder.Property(s => s.AppliedBy).IsRequired().HasMaxLength(450);

        builder.OwnsOne(s => s.GrossCommission, m =>
        {
            m.Property(x => x.Amount).HasColumnName("GrossCommission").HasColumnType("decimal(18,4)").IsRequired();
            m.Property(x => x.Currency).HasColumnName("GrossCurrency").HasMaxLength(3).IsRequired();
        });

        builder.OwnsOne(s => s.ClawbackWithheld, m =>
        {
            m.Property(x => x.Amount).HasColumnName("ClawbackWithheld").HasColumnType("decimal(18,4)").IsRequired();
            m.Property(x => x.Currency).HasColumnName("WithheldCurrency").HasMaxLength(3).IsRequired();
        });

        builder.OwnsOne(s => s.NetPaid, m =>
        {
            m.Property(x => x.Amount).HasColumnName("NetPaid").HasColumnType("decimal(18,4)").IsRequired();
            m.Property(x => x.Currency).HasColumnName("NetCurrency").HasMaxLength(3).IsRequired();
        });

        builder.OwnsOne(s => s.CarryoverRemaining, m =>
        {
            m.Property(x => x.Amount).HasColumnName("CarryoverRemaining").HasColumnType("decimal(18,4)").IsRequired();
            m.Property(x => x.Currency).HasColumnName("CarryoverCurrency").HasMaxLength(3).IsRequired();
        });

        // One settlement per (run, payee, currency). A second row would mean the same debt was
        // collected twice from the same payment — the DB refuses it rather than trusting the handler.
        builder.HasIndex(s => new { s.TenantId, s.PayRunId, s.PayeeId, s.Currency })
            .IsUnique()
            .HasDatabaseName("UX_PayRunSettlements_Run_Payee_Currency");

        builder.HasIndex(s => new { s.TenantId, s.PayeeId, s.AppliedAt })
            .HasDatabaseName("IX_PayRunSettlements_Tenant_Payee_AppliedAt");
    }
}
