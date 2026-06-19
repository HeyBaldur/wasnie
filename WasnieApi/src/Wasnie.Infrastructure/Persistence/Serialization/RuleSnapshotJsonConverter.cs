using System.Text.Json;
using System.Text.Json.Serialization;
using Wasnie.Domain.Compensation.Enums;
using Wasnie.Domain.Compensation.Rules;
using Wasnie.Domain.Compensation.ValueObjects;

namespace Wasnie.Infrastructure.Persistence.Serialization;

public sealed class RuleSnapshotJsonConverter : JsonConverter<RuleSnapshot>
{
    private static readonly JsonSerializerOptions NestedOptions = BuildNestedOptions();

    private static JsonSerializerOptions BuildNestedOptions()
    {
        var opts = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        opts.Converters.Add(new MoneyJsonConverter());
        return opts;
    }

    public override RuleSnapshot Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;

        var ruleId = root.GetProperty("ruleId").GetGuid();
        var planId = root.GetProperty("planId").GetGuid();
        var planVersion = root.GetProperty("planVersion").GetInt32();
        var ruleName = root.GetProperty("ruleName").GetString()!;
        var rateTable = JsonSerializer.Deserialize<RateTable>(
            root.GetProperty("rateTable").GetRawText(), NestedOptions)!;
        var trigger = JsonSerializer.Deserialize<Trigger>(
            root.GetProperty("trigger").GetRawText(), NestedOptions)!;
        var frozenAt = root.GetProperty("frozenAt").GetDateTimeOffset();

        // Backward-compat: credits written before WI-UNITS-MEASUREMENT lack "measurement" in JSON.
        // Default to Revenue/amount/Sum which is what the motor computed for all historic credits.
        Measurement measurement;
        if (root.TryGetProperty("measurement", out var mElem))
            measurement = JsonSerializer.Deserialize<Measurement>(mElem.GetRawText(), NestedOptions)!;
        else
            measurement = new Measurement
            {
                Type = MeasurementType.Revenue,
                SourceField = "amount",
                Aggregation = MeasurementAggregation.Sum,
            };

        return RuleSnapshot.Freeze(ruleId, planId, planVersion, ruleName, rateTable, trigger, frozenAt, measurement: measurement);
    }

    public override void Write(Utf8JsonWriter writer, RuleSnapshot value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString("ruleId", value.RuleId);
        writer.WriteString("planId", value.PlanId);
        writer.WriteNumber("planVersion", value.PlanVersion);
        writer.WriteString("ruleName", value.RuleName);
        writer.WritePropertyName("rateTable");
        JsonSerializer.Serialize(writer, value.RateTable, NestedOptions);
        writer.WritePropertyName("trigger");
        JsonSerializer.Serialize(writer, value.Trigger, NestedOptions);
        writer.WritePropertyName("measurement");
        JsonSerializer.Serialize(writer, value.Measurement, NestedOptions);
        writer.WriteString("frozenAt", value.FrozenAt);
        writer.WriteEndObject();
    }
}
