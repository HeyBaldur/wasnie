using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wasnie.Domain.Compensation.Transactions;

namespace Wasnie.Infrastructure.Persistence.Configurations.Compensation;

public sealed class CompensationTransactionConfiguration : IEntityTypeConfiguration<CompensationTransaction>
{
    public void Configure(EntityTypeBuilder<CompensationTransaction> builder)
    {
        builder.ToTable("CompensationTransactions");

        builder.HasKey(t => t.Id);

        builder.Property(t => t.TenantId).IsRequired();
        builder.Property(t => t.ReferenceNumber).IsRequired().HasMaxLength(200);
        builder.Property(t => t.Quantity).IsRequired().HasDefaultValue(1);
        // Descriptive label (HubSpot deal name / manual / Excel). Nullable — pre-existing rows have none.
        builder.Property(t => t.Description).HasMaxLength(CompensationTransaction.MaxDescriptionLength);
        // Nullable per Decision D: transactions may exist without an assigned payee.
        builder.Property(t => t.TransactionDate).IsRequired();
        builder.Property(t => t.Source).HasConversion<string>().HasMaxLength(50).IsRequired();
        builder.Property(t => t.Status).HasConversion<string>().HasMaxLength(50).IsRequired();
        // What was sold. Descriptive today; ProductSku is the discrete value rule triggers will filter
        // on later, which is why it is its own column rather than text inside the label.
        builder.Property(t => t.ProductName).HasMaxLength(CompensationTransaction.MaxDescriptionLength);
        builder.Property(t => t.ProductSku).HasMaxLength(CompensationTransaction.MaxDescriptionLength);
        // Enrichment output (WI-ENRICHMENT): the resolved category a rule trigger can filter on.
        builder.Property(t => t.Category).HasMaxLength(CompensationTransaction.MaxDescriptionLength);
        builder.Property(t => t.ExternalId).HasMaxLength(500);
        // Admin's explicit plan attribution (manual ingest, multi-plan payees). Nullable: every other
        // origin resolves through PlanAssignmentResolver as before. No FK on purpose — this records
        // what the admin decided; deleting an assignment must not cascade into transaction history.
        builder.Property(t => t.SelectedPlanAssignmentId);
        builder.Property(t => t.IngestedAt).IsRequired();
        builder.Property(t => t.IngestedBy).IsRequired().HasMaxLength(450);
        builder.Property(t => t.UpdatedAt).IsRequired();
        builder.Property(t => t.CancelledBy).HasMaxLength(450);
        builder.Property(t => t.CancelledAt);
        builder.Property(t => t.CancelledReason).HasMaxLength(1000);

        builder.OwnsOne(t => t.Amount, m =>
        {
            m.Property(x => x.Amount).HasColumnName("Amount").HasColumnType("decimal(18,4)").IsRequired();
            m.Property(x => x.Currency).HasColumnName("Currency").HasMaxLength(3).IsRequired();
        });

        // Internal reference uniqueness — filtered to EXCLUDE Cancelled (void) rows so that a voided
        // transaction does NOT block re-creating an active one with the same Reference (WI bulk-void +
        // re-import, Opción B). Two ACTIVE rows with the same (TenantId, ReferenceNumber) are still
        // forbidden. Status is stored as a string; "not cancelled" keeps any future status active.
        builder.HasIndex(t => new { t.TenantId, t.ReferenceNumber })
            .IsUnique()
            .HasFilter("[Status] <> 'Cancelled'")
            .HasDatabaseName("IX_CompensationTransactions_TenantId_ReferenceNumber");

        builder.HasIndex(t => new { t.TenantId, t.PayeeId })
            .HasDatabaseName("IX_CompensationTransactions_TenantId_PayeeId");

        // Idempotency for external systems (HubSpot deal id, etc.). Filtered so manual transactions
        // (null ExternalId) are exempt AND so a voided row no longer blocks re-ingesting the same
        // external id as a new active transaction (WI bulk-void + re-import, Opción B).
        builder.HasIndex(t => new { t.TenantId, t.Source, t.ExternalId })
            .IsUnique()
            .HasFilter("[ExternalId] IS NOT NULL AND [Status] <> 'Cancelled'")
            .HasDatabaseName("IX_CompensationTransactions_TenantId_Source_ExternalId");

        // Read-path indexes (Rule 3.2.2 — every ORDER BY / WHERE column must be indexed).
        builder.HasIndex(t => new { t.TenantId, t.TransactionDate })
            .HasDatabaseName("IX_CompensationTransactions_TenantId_TransactionDate");

        builder.HasIndex(t => new { t.TenantId, t.Status })
            .HasDatabaseName("IX_CompensationTransactions_TenantId_Status");

        builder.HasIndex(t => new { t.TenantId, t.IngestedAt })
            .HasDatabaseName("IX_CompensationTransactions_TenantId_IngestedAt");
    }
}
