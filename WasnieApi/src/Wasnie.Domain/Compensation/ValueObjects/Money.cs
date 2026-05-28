using System.Globalization;
using Wasnie.Domain.Common;
using Wasnie.Domain.Exceptions;

namespace Wasnie.Domain.Compensation.ValueObjects;

public sealed class Money : ValueObject
{
    public decimal Amount { get; }
    public string Currency { get; }

    private Money(decimal amount, string currency)
    {
        // Normalize to 4-decimal internal precision with banker's rounding (§5b.5).
        // Every code path (Of, Add, Subtract, Multiply, Divide, Negate, Abs) goes through
        // this constructor, so all Money values are always stored at ≤4 decimal places.
        Amount = Math.Round(amount, 4, MidpointRounding.ToEven);
        Currency = currency;
    }

    public static Money Of(decimal amount, string currency)
    {
        if (string.IsNullOrWhiteSpace(currency) || currency.Length != 3)
            throw new DomainException("Currency must be a 3-letter ISO code.");

        return new Money(amount, currency.ToUpperInvariant());
    }

    public static Money OfNonNegative(decimal amount, string currency)
    {
        if (amount < 0)
            throw new DomainException("Amount must be non-negative.");

        return Of(amount, currency);
    }

    public static Money Zero(string currency) => Of(0m, currency);

    // ── Arithmetic (each returns a new Money — immutability) ──────────────────

    public Money Add(Money other)
    {
        GuardSameCurrency(this, other);
        return new Money(Amount + other.Amount, Currency);
    }

    public Money Subtract(Money other)
    {
        GuardSameCurrency(this, other);
        return new Money(Amount - other.Amount, Currency);
    }

    // Product is re-normalized to 4-decimal precision via the private constructor (§5b.5).
    public Money Multiply(decimal factor) => new(Amount * factor, Currency);

    public Money Divide(decimal divisor)
    {
        if (divisor == 0m)
            throw new DomainException("Cannot divide Money by zero.");

        return new Money(Amount / divisor, Currency);
    }

    public Money Negate() => new(-Amount, Currency);

    public Money Abs() => new(Math.Abs(Amount), Currency);

    // ── Ordering operators: same-currency only; cross-currency throws (§5b.5) ──
    // == and != are inherited from ValueObject and return false (not throw) when
    // currencies differ — do not call these for ordering of different currencies.

    public static bool operator >(Money left, Money right)
    {
        GuardSameCurrency(left, right);
        return left.Amount > right.Amount;
    }

    public static bool operator <(Money left, Money right)
    {
        GuardSameCurrency(left, right);
        return left.Amount < right.Amount;
    }

    public static bool operator >=(Money left, Money right)
    {
        GuardSameCurrency(left, right);
        return left.Amount >= right.Amount;
    }

    public static bool operator <=(Money left, Money right)
    {
        GuardSameCurrency(left, right);
        return left.Amount <= right.Amount;
    }

    // ── Equality (via ValueObject base) ───────────────────────────────────────

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return Amount;
        yield return Currency;
    }

    // ── Display ───────────────────────────────────────────────────────────────

    // Rounds the 4-decimal stored amount to 2 decimal places for display only.
    // Never mutates the stored Amount (§5b.5).
    public override string ToString()
    {
        var rounded = Math.Round(Amount, 2, MidpointRounding.ToEven);
        return rounded.ToString("F2", CultureInfo.InvariantCulture) + " " + Currency;
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private static void GuardSameCurrency(Money a, Money b)
    {
        if (a.Currency != b.Currency)
            throw new DomainException($"Cannot operate on Money with different currencies: {a.Currency} vs {b.Currency}.");
    }
}
