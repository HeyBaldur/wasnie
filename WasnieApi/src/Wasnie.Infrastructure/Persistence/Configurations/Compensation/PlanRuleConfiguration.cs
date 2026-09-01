using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wasnie.Domain.Compensation.Plans;
using Wasnie.Domain.Compensation.Rules;
using Wasnie.Infrastructure.Persistence.Serialization;

namespace Wasnie.Infrastructure.Persistence.Configurations.Compensation;

public sealed class PlanRuleConfiguration : IEntityTypeConfiguration<Rule>
{
    private static readonly JsonSerializerOptions JsonOptions = BuildJsonOptions();

    private static JsonSerializerOptions BuildJsonOptions()
    {
        var opts = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        opts.Converters.Add(new MoneyJsonConverter());
        return opts;
    }

    public void Configure(EntityTypeBuilder<Rule> builder)
    {
        builder.ToTable("PlanRules");

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();

        builder.Property(r => r.PlanId).IsRequired();
        builder.Property(r => r.Name).IsRequired().HasMaxLength(200);
        builder.Property(r => r.SortOrder).IsRequired();
        builder.Property(r => r.IsActive).IsRequired();

        builder.Property(r => r.Trigger)
            .HasColumnType("nvarchar(max)")
            .HasConversion(
                v => JsonSerializer.Serialize(v, JsonOptions),
                v => JsonSerializer.Deserialize<Trigger>(v, JsonOptions)!,
                JsonComparer<Trigger>());

        builder.Property(r => r.Measurement)
            .HasColumnType("nvarchar(max)")
            .HasConversion(
                v => JsonSerializer.Serialize(v, JsonOptions),
                v => JsonSerializer.Deserialize<Measurement>(v, JsonOptions)!,
                JsonComparer<Measurement>());

        builder.Property(r => r.RateTable)
            .HasColumnType("nvarchar(max)")
            .HasConversion(
                v => JsonSerializer.Serialize(v, JsonOptions),
                v => JsonSerializer.Deserialize<RateTable>(v, JsonOptions)!,
                JsonComparer<RateTable>());

        builder.Property(r => r.Modifier)
            .HasColumnType("nvarchar(max)")
            .HasConversion(
                v => v == null ? null : JsonSerializer.Serialize(v, JsonOptions),
                v => v == null ? null : JsonSerializer.Deserialize<Modifier>(v, JsonOptions),
                NullableJsonComparer<Modifier>());

        builder.Property(r => r.Cap)
            .HasColumnType("nvarchar(max)")
            .HasConversion(
                v => v == null ? null : JsonSerializer.Serialize(v, JsonOptions),
                v => v == null ? null : JsonSerializer.Deserialize<Cap>(v, JsonOptions),
                NullableJsonComparer<Cap>());

        builder.Property(r => r.Floor)
            .HasColumnType("nvarchar(max)")
            .HasConversion(
                v => v == null ? null : JsonSerializer.Serialize(v, JsonOptions),
                v => v == null ? null : JsonSerializer.Deserialize<Floor>(v, JsonOptions),
                NullableJsonComparer<Floor>());

        builder.OwnsOne(r => r.EffectivePeriod, ep =>
        {
            ep.Property(d => d.Start).HasColumnName("EffectivePeriodStart").HasColumnType("date");
            ep.Property(d => d.End).HasColumnName("EffectivePeriodEnd").HasColumnType("date");
        });
        builder.Navigation(r => r.EffectivePeriod).IsRequired(false);

        builder.Property(r => r.Tag).HasColumnType("nvarchar(50)").IsRequired(false);

        // The stop marker. All three nullable, all three null on every rule that exists today —
        // which is what makes this migration inert: nothing changes meaning until someone brakes a
        // rule. StopReason's length is enforced in the domain (Rule.StopReasonMaxLength) so the
        // refusal reaches the browser as a translatable code rather than a truncation at the wall.
        builder.Property(r => r.StoppedAt).IsRequired(false);
        builder.Property(r => r.StoppedBy).HasMaxLength(450).IsRequired(false);
        builder.Property(r => r.StopReason).HasMaxLength(500).IsRequired(false);

        builder.HasIndex(r => new { r.PlanId, r.SortOrder });
    }

    private static ValueComparer<T> JsonComparer<T>() where T : class =>
        new(
            (l, r) => JsonSerializer.Serialize(l, JsonOptions) == JsonSerializer.Serialize(r, JsonOptions),
            v => JsonSerializer.Serialize(v, JsonOptions).GetHashCode(),
            v => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(v, JsonOptions), JsonOptions)!);

    private static ValueComparer<T?> NullableJsonComparer<T>() where T : class =>
        new(
            (l, r) => JsonSerializer.Serialize(l, JsonOptions) == JsonSerializer.Serialize(r, JsonOptions),
            v => v == null ? 0 : JsonSerializer.Serialize(v, JsonOptions).GetHashCode(),
            v => v == null ? null : JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(v, JsonOptions), JsonOptions));
}
