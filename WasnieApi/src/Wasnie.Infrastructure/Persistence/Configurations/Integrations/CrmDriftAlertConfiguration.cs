using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wasnie.Domain.Integrations.Crm;

namespace Wasnie.Infrastructure.Persistence.Configurations.Integrations;

public sealed class CrmDriftAlertConfiguration : IEntityTypeConfiguration<CrmDriftAlert>
{
    public void Configure(EntityTypeBuilder<CrmDriftAlert> builder)
    {
        builder.ToTable("CrmDriftAlerts");

        builder.HasKey(a => a.Id);

        builder.Property(a => a.TenantId).IsRequired();
        builder.Property(a => a.Source).IsRequired().HasMaxLength(50);
        builder.Property(a => a.ExternalDealId).IsRequired().HasMaxLength(100);
        builder.Property(a => a.TransactionId).IsRequired();
        builder.Property(a => a.ReferenceNumber).IsRequired().HasMaxLength(100);
        builder.Property(a => a.TransactionStatus).HasConversion<string>().HasMaxLength(20).IsRequired();

        builder.Property(a => a.AmountChanged).IsRequired();
        // Money columns mirror Money value-object precision elsewhere (decimal(18,4)).
        builder.Property(a => a.OldAmount).HasColumnType("decimal(18,4)").IsRequired();
        builder.Property(a => a.OldCurrency).IsRequired().HasMaxLength(3);
        builder.Property(a => a.NewAmount).HasColumnType("decimal(18,4)").IsRequired();
        builder.Property(a => a.NewCurrency).IsRequired().HasMaxLength(3);

        builder.Property(a => a.DateChanged).IsRequired();
        builder.Property(a => a.OldCloseDate).IsRequired();
        builder.Property(a => a.NewCloseDate).IsRequired();

        builder.Property(a => a.DetectedAt).IsRequired();
        builder.Property(a => a.DetectedBy).IsRequired().HasMaxLength(450);
        builder.Property(a => a.ResolvedAt);
        builder.Property(a => a.ResolvedBy).HasMaxLength(450);

        // At most ONE unresolved alert per (tenant, source, deal, transaction). Filtered so resolved rows
        // (history) don't block a fresh alert if the same deal drifts again later.
        builder.HasIndex(a => new { a.TenantId, a.Source, a.ExternalDealId, a.TransactionId })
            .IsUnique()
            .HasFilter("[ResolvedAt] IS NULL")
            .HasDatabaseName("IX_CrmDriftAlerts_TenantId_Source_Deal_Transaction_Unresolved");

        // Dashboard query: unresolved alerts for the tenant, newest first.
        builder.HasIndex(a => new { a.TenantId, a.ResolvedAt, a.DetectedAt })
            .HasDatabaseName("IX_CrmDriftAlerts_TenantId_ResolvedAt_DetectedAt");
    }
}
