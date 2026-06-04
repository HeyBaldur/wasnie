using System.Globalization;
using Wasnie.Application.Common.Constants;
using Wasnie.Application.Models.Imports;

namespace Wasnie.Application.Services.Imports;

/// <summary>
/// Shared, stateless field-level validators for transaction import and update wizards.
/// Both wizards MUST use these methods so validation is identical across flows.
/// See 14-forbidden-patterns.md: duplicate validation logic across wizards is forbidden.
/// </summary>
public static class TransactionFieldValidators
{
    public static readonly DateOnly MinTransactionDate = new(2000, 1, 1);

    /// <summary>
    /// Returns null if <paramref name="currency"/> is a known ISO 4217 code; otherwise an error issue.
    /// The accepted set is <see cref="CurrencyConstants.KnownCurrencies"/> — same list used by plan creation.
    /// </summary>
    public static ValidationIssue? ValidateCurrency(string currency)
    {
        if (!CurrencyConstants.KnownCurrencies.Contains(currency))
            return MakeError("currency",
                $"Currency '{currency}' is not a recognized currency code. Examples: EUR, USD, GBP, PLN, CHF.",
                IssueCategory.Format);
        return null;
    }

    /// <summary>
    /// Returns null if <paramref name="amountStr"/> is a valid positive decimal; otherwise an error issue.
    /// <paramref name="parsed"/> is set to the parsed value when valid, or 0 otherwise.
    /// </summary>
    public static ValidationIssue? ValidateAmount(string amountStr, out decimal parsed)
    {
        if (!decimal.TryParse(amountStr, NumberStyles.Number, CultureInfo.InvariantCulture, out parsed))
            return MakeError("amount",
                $"Amount '{amountStr}' is not a valid number.",
                IssueCategory.Format);

        if (parsed <= 0)
            return MakeError("amount",
                $"Amount '{parsed.ToString(CultureInfo.InvariantCulture)}' must be greater than zero.",
                IssueCategory.Format);

        return null;
    }

    /// <summary>
    /// Returns null if <paramref name="dateStr"/> is a valid ISO 8601 date within allowed range;
    /// otherwise the first error issue. <paramref name="parsed"/> is set when valid.
    /// </summary>
    public static ValidationIssue? ValidateTransactionDate(string dateStr, DateOnly today, out DateOnly parsed)
    {
        if (!TryParseDate(dateStr, out parsed))
            return MakeError("transactionDate",
                $"Date '{dateStr}' is not in the required ISO 8601 format (YYYY-MM-DD). Examples: 2026-05-15, 2026-12-31.",
                IssueCategory.Format);

        if (parsed < MinTransactionDate)
            return MakeError("transactionDate",
                $"Transaction date '{parsed.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}' is before the minimum date {MinTransactionDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}.",
                IssueCategory.Format);

        if (parsed > today)
            return MakeError("transactionDate",
                $"Transaction date '{parsed.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}' is in the future.",
                IssueCategory.Format);

        return null;
    }

    /// <summary>
    /// Parses an ISO 8601 date string (yyyy-MM-dd only). Returns false for any other format.
    /// </summary>
    public static bool TryParseDate(string s, out DateOnly result) =>
        DateOnly.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out result);

    private static ValidationIssue MakeError(string field, string msg, IssueCategory cat) =>
        new() { Field = field, Message = msg, Severity = IssueSeverity.Error, Category = cat };
}
