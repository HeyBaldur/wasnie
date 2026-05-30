using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Wasnie.Domain.Compensation.ValueObjects;
using Wasnie.Domain.Exceptions;

namespace Wasnie.Infrastructure.Persistence.Serialization;

// Replaces the [JsonConstructor] attribute removed from Money.cs (Rule 1.5 fix).
// The stored format is {"amount":<decimal>,"currency":"<ISO3>"} — byte-compatible
// with what [JsonConstructor] + JsonSerializerDefaults.Web produced previously.
public sealed class MoneyJsonConverter : JsonConverter<Money>
{
    public override Money Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
            throw new JsonException("Expected start of Money JSON object.");

        decimal amount = 0;
        string currency = string.Empty;

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
                break;

            if (reader.TokenType != JsonTokenType.PropertyName)
                throw new JsonException("Expected property name in Money JSON object.");

            var propertyName = reader.GetString()!;
            reader.Read();

            if (propertyName.Equals("amount", StringComparison.OrdinalIgnoreCase))
            {
                amount = reader.TokenType == JsonTokenType.String
                    ? decimal.Parse(reader.GetString()!, CultureInfo.InvariantCulture)
                    : reader.GetDecimal();
            }
            else if (propertyName.Equals("currency", StringComparison.OrdinalIgnoreCase))
            {
                currency = reader.GetString() ?? string.Empty;
            }
            else
            {
                reader.Skip();
            }
        }

        try
        {
            return Money.Of(amount, currency);
        }
        catch (DomainException ex)
        {
            throw new JsonException(ex.Message, ex);
        }
    }

    public override void Write(Utf8JsonWriter writer, Money value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteNumber("amount", value.Amount);
        writer.WriteString("currency", value.Currency);
        writer.WriteEndObject();
    }
}
