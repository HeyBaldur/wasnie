using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wasnie.Domain.Compensation.Ledger;

namespace Wasnie.Infrastructure.Persistence.Configurations.Compensation;

public sealed class PayeeLedgerEntryConfiguration : IEntityTypeConfiguration<PayeeLedgerEntry>
{
    public void Configure(EntityTypeBuilder<PayeeLedgerEntry> builder)
    {
        builder.ToTable("PayeeLedgerEntries");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.TenantId).IsRequired();
        builder.Property(e => e.PayeeId).IsRequired();

        // Enums as strings: a ledger row must stay readable in a DB console years from now, and an
        // int would silently re-map if the enum ever gains a member in the middle.
        builder.Property(e => e.Origin).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(e => e.TransactionType).HasConversion<string>().HasMaxLength(40).IsRequired();
        builder.Property(e => e.SourceType).HasConversion<string>().HasMaxLength(40).IsRequired(false);

        builder.OwnsOne(e => e.Amount, m =>
        {
            m.Property(x => x.Amount).HasColumnName("Amount").HasColumnType("decimal(18,4)").IsRequired();
            m.Property(x => x.Currency).HasColumnName("Currency").HasMaxLength(3).IsRequired();
        });

        builder.Property(e => e.Justification).IsRequired().HasMaxLength(1000);

        builder.Property(e => e.SourceTransactionId).IsRequired(false);
        builder.Property(e => e.SourcePayRunId).IsRequired(false);
        builder.Property(e => e.SourceExternalDealId).HasMaxLength(100).IsRequired(false);

        builder.Property(e => e.SourceCommissionAmount).HasColumnType("decimal(18,4)").IsRequired(false);
        builder.Property(e => e.DaysActive).IsRequired(false);
        builder.Property(e => e.MaturationDays).IsRequired(false);
        builder.Property(e => e.SourcePlanId).IsRequired(false);
        builder.Property(e => e.EventDate).IsRequired(false);

        builder.Property(e => e.CreatedAt).IsRequired();
        builder.Property(e => e.CreatedBy).IsRequired().HasMaxLength(450);

        // The read that matters: one payee's ledger in one currency, newest first.
        builder.HasIndex(e => new { e.TenantId, e.PayeeId, e.CreatedAt })
            .HasDatabaseName("IX_PayeeLedgerEntries_Tenant_Payee_CreatedAt");

        // Tracing a clawback back to the transaction that caused it.
        builder.HasIndex(e => e.SourceTransactionId)
            .HasFilter("[SourceTransactionId] IS NOT NULL")
            .HasDatabaseName("IX_PayeeLedgerEntries_SourceTransaction");

        // ONE churn debit per (transaction, plan). The trigger also checks in code before writing, but a
        // read-then-write check cannot survive two syncs racing — this index can. It is scoped to DealChurn
        // so it constrains nothing else in the ledger, and to the plan because a transaction credited under
        // two plans legitimately produces two debits with two different maturation windows.
        builder.HasIndex(e => new { e.SourceTransactionId, e.SourcePlanId })
            .IsUnique()
            .HasFilter("[SourceType] = 'DealChurn' AND [SourceTransactionId] IS NOT NULL AND [SourcePlanId] IS NOT NULL")
            .HasDatabaseName("UX_PayeeLedgerEntries_ChurnPerTransactionPlan");
    }
}
