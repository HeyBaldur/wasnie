namespace Wasnie.Application.Features.Subscription;

/// <summary>
/// Stripe sends amounts as integers in the currency's smallest unit. For most currencies that is cents (÷100), but
/// not for all: "zero-decimal" currencies (JPY, KRW…) are already whole units and "three-decimal" ones (KWD, BHD…)
/// are in thousandths. Dividing everything by 100 would show a ¥1,000 invoice as ¥10.
/// Source: https://docs.stripe.com/currencies#zero-decimal and #three-decimal.
/// </summary>
public static class StripeAmount
{
    private static readonly HashSet<string> ZeroDecimal = new(StringComparer.OrdinalIgnoreCase)
    {
        "bif", "clp", "djf", "gnf", "jpy", "kmf", "krw", "mga", "pyg", "rwf", "ugx", "vnd", "vuv", "xaf", "xof", "xpf",
    };

    private static readonly HashSet<string> ThreeDecimal = new(StringComparer.OrdinalIgnoreCase)
    {
        "bhd", "jod", "kwd", "omr", "tnd",
    };

    public static decimal FromMinorUnits(long amount, string currency)
    {
        if (ZeroDecimal.Contains(currency))
            return amount;
        if (ThreeDecimal.Contains(currency))
            return amount / 1000m;
        return amount / 100m;
    }
}
