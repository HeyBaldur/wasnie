using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wasnie.Domain.Compensation.Payouts;

namespace Wasnie.Infrastructure.Persistence.Configurations.Compensation;

public sealed class PayRunConfiguration : IEntityTypeConfiguration<PayRun>
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    public void Configure(EntityTypeBuilder<PayRun> builder)
    {
        builder.ToTable("PayRuns");

        builder.HasKey(r => r.Id);

        builder.Property(r => r.TenantId).IsRequired();
        builder.Property(r => r.PeriodStart).HasColumnType("date").IsRequired();
        builder.Property(r => r.PeriodEnd).HasColumnType("date").IsRequired();
        builder.Property(r => r.Status).HasConversion<string>().HasMaxLength(50).IsRequired();

        builder.Property(r => r.CreatedAt).IsRequired();
        builder.Property(r => r.CreatedBy).IsRequired().HasMaxLength(450);
        builder.Property(r => r.ApprovedAt).IsRequired(false);
        builder.Property(r => r.ApprovedBy).IsRequired(false).HasMaxLength(450);
        builder.Property(r => r.PaidAt).IsRequired(false);
        builder.Property(r => r.PaidBy).IsRequired(false).HasMaxLength(450);

        builder.Property(r => r.PayeeCount).IsRequired();
        builder.Property(r => r.PaidPayeeCount).IsRequired();
        builder.Property(r => r.ZeroPayoutCount).IsRequired();

        builder.Property(r => r.TotalAmounts)
            .HasColumnType("nvarchar(max)")
            .IsRequired()
            .HasConversion(
                v => JsonSerializer.Serialize(v, JsonOptions),
                v => JsonSerializer.Deserialize<Dictionary<string, decimal>>(v, JsonOptions)
                     ?? new Dictionary<string, decimal>());

        // Enforces one PayRun per period per tenant.
        // Named uniquely — created by migration A6_AddPayRun as HasIndex cannot use DateOnly
        // owned columns, but these ARE direct columns on PayRun so HasIndex works here.
        builder.HasIndex(r => new { r.TenantId, r.PeriodStart, r.PeriodEnd })
            .IsUnique()
            .HasDatabaseName("IX_PayRuns_Unique");

        builder.HasIndex(r => r.TenantId)
            .HasDatabaseName("IX_PayRuns_TenantId");
    }
}
