using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wasnie.Domain.Compensation.Credits;
using Wasnie.Domain.Compensation.ValueObjects;
using Wasnie.Infrastructure.Persistence.Serialization;

namespace Wasnie.Infrastructure.Persistence.Configurations.Compensation;

public sealed class CreditConfiguration : IEntityTypeConfiguration<Credit>
{
    private static readonly JsonSerializerOptions JsonOptions = BuildJsonOptions();

    private static JsonSerializerOptions BuildJsonOptions()
    {
        var opts = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        opts.Converters.Add(new MoneyJsonConverter());
        opts.Converters.Add(new RuleSnapshotJsonConverter());
        return opts;
    }

    public void Configure(EntityTypeBuilder<Credit> builder)
    {
        builder.ToTable("Credits");

        builder.HasKey(c => c.Id);

        builder.Property(c => c.TenantId).IsRequired();
        builder.Property(c => c.TransactionId).IsRequired();
        builder.Property(c => c.PayeeId).IsRequired();
        builder.Property(c => c.PlanId).IsRequired();
        builder.Property(c => c.RuleId).IsRequired();
        builder.Property(c => c.Role).HasConversion<string>().HasMaxLength(50).IsRequired();
        builder.Property(c => c.AllocatedAt).IsRequired();
        builder.Property(c => c.AllocatedBy).IsRequired().HasMaxLength(450);
        builder.Property(c => c.SupersededAt).IsRequired(false);
        builder.Property(c => c.SupersededBy).HasColumnType("nvarchar(max)").IsRequired(false);

        builder.OwnsOne(c => c.OriginalAmount, m =>
        {
            m.Property(x => x.Amount).HasColumnName("OriginalAmount").HasColumnType("decimal(18,4)").IsRequired();
            m.Property(x => x.Currency).HasColumnName("OriginalCurrency").HasMaxLength(3).IsRequired();
        });

        builder.OwnsOne(c => c.CreditedAmount, m =>
        {
            m.Property(x => x.Amount).HasColumnName("CreditedAmount").HasColumnType("decimal(18,4)").IsRequired();
            m.Property(x => x.Currency).HasColumnName("CreditedCurrency").HasMaxLength(3).IsRequired();
        });

        builder.OwnsOne(c => c.SplitPercentage, pct =>
        {
            pct.Property(x => x.Value).HasColumnName("SplitPercentage").HasColumnType("decimal(5,4)").IsRequired();
        });

        builder.Property(c => c.RuleSnapshot)
            .HasColumnType("nvarchar(max)")
            .HasConversion(
                v => JsonSerializer.Serialize(v, JsonOptions),
                v => JsonSerializer.Deserialize<RuleSnapshot>(v, JsonOptions)!);

        builder.HasIndex(c => new { c.TenantId, c.TransactionId, c.PayeeId });
        builder.HasIndex(c => new { c.TenantId, c.SupersededAt })
            .HasFilter("[SupersededAt] IS NULL");
    }
}
