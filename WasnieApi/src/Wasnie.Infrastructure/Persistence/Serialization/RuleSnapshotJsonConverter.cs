using System.Text.Json;
using System.Text.Json.Serialization;
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

        return RuleSnapshot.Freeze(ruleId, planId, planVersion, ruleName, rateTable, trigger, frozenAt);
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
        writer.WriteString("frozenAt", value.FrozenAt);
        writer.WriteEndObject();
    }
}
