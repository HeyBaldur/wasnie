using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wasnie.Domain.Integrations.Crm;

namespace Wasnie.Infrastructure.Persistence.Configurations.Integrations;

public sealed class DealLostAlertConfiguration : IEntityTypeConfiguration<DealLostAlert>
{
    public void Configure(EntityTypeBuilder<DealLostAlert> builder)
    {
        builder.ToTable("DealLostAlerts");

        builder.HasKey(a => a.Id);

        builder.Property(a => a.TenantId).IsRequired();
        builder.Property(a => a.Source).IsRequired().HasMaxLength(50);
        builder.Property(a => a.ExternalDealId).IsRequired().HasMaxLength(100);
        builder.Property(a => a.TransactionId).IsRequired();
        builder.Property(a => a.ReferenceNumber).IsRequired().HasMaxLength(100);
        builder.Property(a => a.TransactionStatus).HasConversion<string>().HasMaxLength(20).IsRequired();

        // Money column mirrors Money value-object precision elsewhere (decimal(18,4)).
        builder.Property(a => a.CommissionAmount).HasColumnType("decimal(18,4)").IsRequired();
        builder.Property(a => a.CommissionCurrency).IsRequired().HasMaxLength(3);

        builder.Property(a => a.DetectedAt).IsRequired();
        builder.Property(a => a.DetectedBy).IsRequired().HasMaxLength(450);
        builder.Property(a => a.ResolvedAt);
        builder.Property(a => a.ResolvedBy).HasMaxLength(450);

        // At most ONE unresolved alert per (tenant, source, transaction). Filtered so resolved rows
        // (history) don't block a fresh alert if the same deal is lost again after being re-imported.
        builder.HasIndex(a => new { a.TenantId, a.Source, a.TransactionId })
            .IsUnique()
            .HasFilter("[ResolvedAt] IS NULL")
            .HasDatabaseName("IX_DealLostAlerts_TenantId_Source_Transaction_Unresolved");

        // Dashboard query: unresolved alerts for the tenant, newest first.
        builder.HasIndex(a => new { a.TenantId, a.ResolvedAt, a.DetectedAt })
            .HasDatabaseName("IX_DealLostAlerts_TenantId_ResolvedAt_DetectedAt");
    }
}
