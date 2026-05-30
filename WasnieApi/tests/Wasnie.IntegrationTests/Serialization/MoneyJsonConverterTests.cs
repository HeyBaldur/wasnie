using System.Text.Json;
using FluentAssertions;
using Wasnie.Domain.Compensation.ValueObjects;
using Wasnie.Domain.Exceptions;
using Wasnie.Infrastructure.Persistence.Serialization;

namespace Wasnie.IntegrationTests.Serialization;

public sealed class MoneyJsonConverterTests
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new MoneyJsonConverter() }
    };

    // ── Serialize ─────────────────────────────────────────────────────────────

    [Fact]
    public void Serialize_Money_ProducesExpectedJson()
    {
        var money = Money.Of(100m, "EUR");
        var json = JsonSerializer.Serialize(money, Options);
        json.Should().Be("{\"amount\":100,\"currency\":\"EUR\"}");
    }

    [Fact]
    public void Serialize_FourDecimalMoney_PreservesFourDecimals()
    {
        var money = Money.Of(1.2345m, "USD");
        var json = JsonSerializer.Serialize(money, Options);
        json.Should().Be("{\"amount\":1.2345,\"currency\":\"USD\"}");
    }

    [Fact]
    public void Serialize_NegativeMoney_ProducesNegativeAmount()
    {
        var money = Money.Of(-50.75m, "EUR");
        var json = JsonSerializer.Serialize(money, Options);
        json.Should().Be("{\"amount\":-50.75,\"currency\":\"EUR\"}");
    }

    [Fact]
    public void Serialize_ZeroMoney_ProducesZeroAmount()
    {
        var money = Money.Zero("PLN");
        var json = JsonSerializer.Serialize(money, Options);
        json.Should().Be("{\"amount\":0,\"currency\":\"PLN\"}");
    }

    // ── Deserialize ───────────────────────────────────────────────────────────

    [Fact]
    public void Deserialize_ValidJson_ReturnsCorrectMoney()
    {
        var json = "{\"amount\":1234.5678,\"currency\":\"EUR\"}";
        var money = JsonSerializer.Deserialize<Money>(json, Options)!;
        money.Amount.Should().Be(1234.5678m);
        money.Currency.Should().Be("EUR");
    }

    [Fact]
    public void Deserialize_BackwardCompatJson_ExistingStoredFormat()
    {
        // This is the exact format that [JsonConstructor] + JsonSerializerDefaults.Web produced.
        // Deserializing it MUST succeed so existing persisted rows are not broken.
        var json = "{\"amount\":1000.0,\"currency\":\"EUR\"}";
        var money = JsonSerializer.Deserialize<Money>(json, Options)!;
        money.Amount.Should().Be(1000m);
        money.Currency.Should().Be("EUR");
    }

    [Fact]
    public void Deserialize_BackwardCompat_FourDecimalAmount_Preserves()
    {
        var json = "{\"amount\":999.9999,\"currency\":\"USD\"}";
        var money = JsonSerializer.Deserialize<Money>(json, Options)!;
        money.Amount.Should().Be(999.9999m);
        money.Currency.Should().Be("USD");
    }

    [Fact]
    public void Deserialize_AmountAsString_Succeeds()
    {
        // JsonSerializerDefaults.Web allows AllowReadingFromString for numbers.
        var json = "{\"amount\":\"100.50\",\"currency\":\"USD\"}";
        var money = JsonSerializer.Deserialize<Money>(json, Options)!;
        money.Amount.Should().Be(100.50m);
        money.Currency.Should().Be("USD");
    }

    [Fact]
    public void Deserialize_PropertyNamesAreCaseInsensitive()
    {
        var json = "{\"Amount\":750.0,\"Currency\":\"GBP\"}";
        var money = JsonSerializer.Deserialize<Money>(json, Options)!;
        money.Amount.Should().Be(750m);
        money.Currency.Should().Be("GBP");
    }

    [Fact]
    public void Deserialize_ExtraUnknownProperties_AreIgnored()
    {
        var json = "{\"amount\":50.0,\"currency\":\"EUR\",\"unknown\":\"ignored\"}";
        var money = JsonSerializer.Deserialize<Money>(json, Options)!;
        money.Amount.Should().Be(50m);
        money.Currency.Should().Be("EUR");
    }

    [Fact]
    public void Deserialize_InvalidCurrency_ThrowsDomainException()
    {
        var json = "{\"amount\":100.0,\"currency\":\"EU\"}";
        var act = () => JsonSerializer.Deserialize<Money>(json, Options);
        act.Should().Throw<JsonException>().WithInnerException<DomainException>();
    }

    // ── Round-trips ───────────────────────────────────────────────────────────

    [Fact]
    public void RoundTrip_StandardMoney_PreservesValue()
    {
        var original = Money.Of(1500m, "EUR");
        var json = JsonSerializer.Serialize(original, Options);
        var deserialized = JsonSerializer.Deserialize<Money>(json, Options)!;
        deserialized.Should().Be(original);
    }

    [Fact]
    public void RoundTrip_NegativeMoney_PreservesValue()
    {
        var original = Money.Of(-999.99m, "USD");
        var json = JsonSerializer.Serialize(original, Options);
        var deserialized = JsonSerializer.Deserialize<Money>(json, Options)!;
        deserialized.Should().Be(original);
    }

    [Fact]
    public void RoundTrip_ZeroMoney_PreservesValue()
    {
        var original = Money.Zero("PLN");
        var json = JsonSerializer.Serialize(original, Options);
        var deserialized = JsonSerializer.Deserialize<Money>(json, Options)!;
        deserialized.Should().Be(original);
    }

    [Fact]
    public void RoundTrip_FourDecimalMoney_PreservesExactValue()
    {
        var original = Money.Of(1.2345m, "CHF");
        var json = JsonSerializer.Serialize(original, Options);
        var deserialized = JsonSerializer.Deserialize<Money>(json, Options)!;
        deserialized.Amount.Should().Be(1.2345m);
        deserialized.Currency.Should().Be("CHF");
    }

    [Fact]
    public void RoundTrip_MultipleCurrencies_EachPreservesValue()
    {
        var currencies = new[] { "EUR", "USD", "PLN", "GBP", "CHF" };
        foreach (var currency in currencies)
        {
            var original = Money.Of(1234.5678m, currency);
            var json = JsonSerializer.Serialize(original, Options);
            var deserialized = JsonSerializer.Deserialize<Money>(json, Options)!;
            deserialized.Should().Be(original, $"currency {currency} must round-trip correctly");
        }
    }

    // ── Nested object (Cap containing Money) ─────────────────────────────────

    [Fact]
    public void RoundTrip_NestedMoneyInCap_PreservesValue()
    {
        var cap = new { _schema = 1, amount = Money.Of(5000m, "EUR"), scope = 0 };
        var json = JsonSerializer.Serialize(cap, Options);
        var parsed = JsonDocument.Parse(json);
        var amountNode = parsed.RootElement.GetProperty("amount");
        amountNode.GetProperty("amount").GetDecimal().Should().Be(5000m);
        amountNode.GetProperty("currency").GetString().Should().Be("EUR");
    }
}
