using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wasnie.Domain.Compensation.Payouts;
using Wasnie.Domain.Compensation.ValueObjects;

namespace Wasnie.Infrastructure.Persistence.Configurations.Compensation;

public sealed class PayoutLineConfiguration : IEntityTypeConfiguration<PayoutLine>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public void Configure(EntityTypeBuilder<PayoutLine> builder)
    {
        builder.ToTable("PayoutLines");

        builder.HasKey(l => l.Id);

        builder.Property(l => l.PayoutId).IsRequired();
        builder.Property(l => l.CreditId).IsRequired();
        builder.Property(l => l.RuleId).IsRequired();
        builder.Property(l => l.RuleName).IsRequired().HasMaxLength(200);

        builder.OwnsOne(l => l.BaseAmount, m =>
        {
            m.Property(x => x.Amount).HasColumnName("BaseAmount").HasColumnType("decimal(18,4)").IsRequired();
            m.Property(x => x.Currency).HasColumnName("BaseCurrency").HasMaxLength(3).IsRequired();
        });

        builder.OwnsOne(l => l.CommissionAmount, m =>
        {
            m.Property(x => x.Amount).HasColumnName("CommissionAmount").HasColumnType("decimal(18,4)").IsRequired();
            m.Property(x => x.Currency).HasColumnName("CommissionCurrency").HasMaxLength(3).IsRequired();
        });

        builder.Property(l => l.AppliedModifiers)
            .HasColumnType("nvarchar(max)")
            .HasConversion(
                v => JsonSerializer.Serialize(v, JsonOptions),
                v => JsonSerializer.Deserialize<List<ModifierApplication>>(v, JsonOptions) ?? new List<ModifierApplication>());

        builder.HasIndex(l => l.PayoutId);
    }
}
