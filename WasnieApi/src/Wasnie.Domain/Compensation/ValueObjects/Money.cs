using System.Globalization;
using System.Text.Json.Serialization;
using Wasnie.Domain.Common;
using Wasnie.Domain.Exceptions;

namespace Wasnie.Domain.Compensation.ValueObjects;

public sealed class Money : ValueObject
{
    public decimal Amount { get; }
    public string Currency { get; }

    [JsonConstructor]
    private Money(decimal amount, string currency)
    {
        Amount = amount;
        Currency = currency;
    }

    public static Money Of(decimal amount, string currency)
    {
        if (string.IsNullOrWhiteSpace(currency) || currency.Length != 3)
        {
            throw new DomainException("Currency must be a 3-letter ISO code.");
        }

        return new Money(amount, currency.ToUpperInvariant());
    }

    public static Money OfNonNegative(decimal amount, string currency)
    {
        if (amount < 0)
        {
            throw new DomainException("Amount must be non-negative.");
        }

        return Of(amount, currency);
    }

    public static Money Zero(string currency) => Of(0m, currency);

    public Money Add(Money other)
    {
        GuardSameCurrency(other);
        return new Money(Amount + other.Amount, Currency);
    }

    public Money Subtract(Money other)
    {
        GuardSameCurrency(other);
        return new Money(Amount - other.Amount, Currency);
    }

    public Money Multiply(decimal factor) => new(Amount * factor, Currency);

    public Money Divide(decimal divisor)
    {
        if (divisor == 0m)
        {
            throw new DomainException("Cannot divide Money by zero.");
        }

        return new Money(Amount / divisor, Currency);
    }

    private void GuardSameCurrency(Money other)
    {
        if (Currency != other.Currency)
        {
            throw new DomainException($"Cannot operate on Money with different currencies: {Currency} vs {other.Currency}.");
        }
    }

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return Amount;
        yield return Currency;
    }

    public override string ToString()
    {
        var rounded = Math.Round(Amount, 2, MidpointRounding.ToEven);
        return rounded.ToString("F2", CultureInfo.InvariantCulture) + " " + Currency;
    }
}
