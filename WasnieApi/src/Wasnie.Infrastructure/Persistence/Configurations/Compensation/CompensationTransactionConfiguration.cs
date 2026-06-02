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
        // Nullable per Decision D: transactions may exist without an assigned payee.
        builder.Property(t => t.TransactionDate).IsRequired();
        builder.Property(t => t.Source).HasConversion<string>().HasMaxLength(50).IsRequired();
        builder.Property(t => t.Status).HasConversion<string>().HasMaxLength(50).IsRequired();
        builder.Property(t => t.ExternalId).HasMaxLength(500);
        builder.Property(t => t.IngestedAt).IsRequired();
        builder.Property(t => t.IngestedBy).IsRequired().HasMaxLength(450);
        builder.Property(t => t.UpdatedAt).IsRequired();

        builder.OwnsOne(t => t.Amount, m =>
        {
            m.Property(x => x.Amount).HasColumnName("Amount").HasColumnType("decimal(18,4)").IsRequired();
            m.Property(x => x.Currency).HasColumnName("Currency").HasMaxLength(3).IsRequired();
        });

        // Internal reference uniqueness (unchanged).
        builder.HasIndex(t => new { t.TenantId, t.ReferenceNumber })
            .IsUnique()
            .HasDatabaseName("IX_CompensationTransactions_TenantId_ReferenceNumber");

        builder.HasIndex(t => new { t.TenantId, t.PayeeId })
            .HasDatabaseName("IX_CompensationTransactions_TenantId_PayeeId");

        // Idempotency: external systems cannot re-ingest the same transaction.
        // Filtered so manual transactions (null ExternalId) are exempt.
        builder.HasIndex(t => new { t.TenantId, t.Source, t.ExternalId })
            .IsUnique()
            .HasFilter("[ExternalId] IS NOT NULL")
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
