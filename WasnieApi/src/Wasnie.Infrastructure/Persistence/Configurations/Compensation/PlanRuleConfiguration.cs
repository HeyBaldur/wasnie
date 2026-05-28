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
