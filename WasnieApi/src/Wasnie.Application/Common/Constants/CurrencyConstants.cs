namespace Wasnie.Application.Common.Constants;

/// <summary>
/// V1 whitelist of accepted ISO 4217 currency codes.
/// Used at every currency entry point: transaction imports, updates, plan creation.
/// To add a currency: append to KnownCurrencies and redeploy. No migration needed.
/// </summary>
public static class CurrencyConstants
{
    public static readonly HashSet<string> KnownCurrencies = new(StringComparer.OrdinalIgnoreCase)
    {
        "EUR", "USD", "GBP", "PLN", "CHF", "JPY", "CAD", "AUD",
        "NOK", "SEK", "DKK", "CZK", "HUF", "RON", "BGN", "MXN", "BRL",
    };
}
