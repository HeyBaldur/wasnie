using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Common.Abstractions;
using Wasnie.Application.Common.Constants;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Models.Imports;
using Wasnie.Application.Services.Imports;
using Wasnie.Domain.Compensation.Enums;

namespace Wasnie.Infrastructure.Services.Imports;

public sealed class TransactionImportValidationService(
    IApplicationDbContext db,
    IClock clock,
    IFieldRequirementService fieldRequirements)
    : ITransactionImportValidationService
{
    private static readonly Regex CurrencyRegex = new(
        @"^[A-Z]{3}$",
        RegexOptions.Compiled,
        TimeSpan.FromMilliseconds(100));

    private static readonly string[] DateFormats =
        ["yyyy-MM-dd", "MM/dd/yyyy", "dd/MM/yyyy", "M/d/yyyy", "d/M/yyyy", "yyyy/MM/dd"];

    private static readonly DateOnly MinDate = new(2000, 1, 1);

    public async Task<List<TransactionRowValidationResult>> ValidateAsync(
        List<Dictionary<string, string>> rows,
        TransactionImportColumnMapping mapping,
        CancellationToken ct = default)
    {
        // Pre-load per-tenant field requirement for PayeeId (Decision D).
        var payeeIdRequired = await fieldRequirements.IsRequiredAsync(
            TransactionFieldNames.Entity, TransactionFieldNames.PayeeId, ct);

        // Pre-load all data up front to avoid N+1 queries.
        // Include IsActive so we can warn on inactive payees (Decision 12).
        var payeesByCode = await db.Payees
            .Select(p => new { p.EmployeeCode, p.Id, p.IsActive })
            .ToDictionaryAsync(p => p.EmployeeCode, StringComparer.OrdinalIgnoreCase, ct);

        var existingReferenceNumbers = new HashSet<string>(
            await db.CompensationTransactions
                .Select(t => t.ReferenceNumber)
                .ToListAsync(ct),
            StringComparer.OrdinalIgnoreCase);

        var existingExternalIds = new HashSet<string>(
            await db.CompensationTransactions
                .Where(t => t.Source == TransactionSource.EtlImport && t.ExternalId != null)
                .Select(t => t.ExternalId!)
                .ToListAsync(ct),
            StringComparer.OrdinalIgnoreCase);

        // Track within-file duplicates.
        var fileReferenceNumbers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var fileExternalIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var today = DateOnly.FromDateTime(clock.UtcNow);
        var results = new List<TransactionRowValidationResult>(rows.Count);

        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            var rowNum = i + 1;
            var issues = new List<ValidationIssue>();

            // ── referenceNumber ───────────────────────────────────────────────
            var referenceNumber = GetField(row, mapping.ReferenceNumberColumn);
            if (string.IsNullOrWhiteSpace(referenceNumber))
            {
                issues.Add(Error("referenceNumber", "Reference number is required.", IssueCategory.Required));
            }
            else if (fileReferenceNumbers.Contains(referenceNumber))
            {
                issues.Add(Error("referenceNumber", $"Reference number '{referenceNumber}' appears more than once in this file.", IssueCategory.Reference));
            }
            else if (existingReferenceNumbers.Contains(referenceNumber))
            {
                issues.Add(Error("referenceNumber", $"Reference number '{referenceNumber}' was already imported. This row will be skipped.", IssueCategory.Reference));
            }
            else
            {
                fileReferenceNumbers.Add(referenceNumber);
            }

            // ── payeeCode ─────────────────────────────────────────────────────
            var payeeCode = GetField(row, mapping.PayeeCodeColumn);
            if (string.IsNullOrWhiteSpace(payeeCode))
            {
                // Blank payeeCode: error if required, silent if Optional (Decision D).
                if (payeeIdRequired)
                    issues.Add(Error("payeeCode", "Payee code is required. Add it to your file or set Payee field to Optional in Settings.", IssueCategory.Required));
                // If Optional, null PayeeId is accepted — no issue emitted.
            }
            else if (!payeesByCode.TryGetValue(payeeCode, out var matchedPayee))
            {
                issues.Add(Error("payeeCode", $"Payee code '{payeeCode}' not found in this tenant. Create the payee first or correct the code in your file.", IssueCategory.Reference));
            }
            else if (!matchedPayee.IsActive)
            {
                // Decision 12: inactive payee match → Warning; row is imported as historical assignment.
                issues.Add(Warn("payeeCode", $"Payee '{payeeCode}' is inactive — assignment will be historical.", IssueCategory.Reference));
            }

            // ── amount ────────────────────────────────────────────────────────
            var amountStr = GetField(row, mapping.AmountColumn);
            if (!decimal.TryParse(amountStr, System.Globalization.NumberStyles.Number,
                    System.Globalization.CultureInfo.InvariantCulture, out var amount))
            {
                issues.Add(Error("amount", $"Amount '{amountStr}' is not a valid number.", IssueCategory.Format));
            }
            else if (amount <= 0)
            {
                issues.Add(Error("amount", $"Amount '{amount.ToString(System.Globalization.CultureInfo.InvariantCulture)}' must be greater than zero.", IssueCategory.Format));
            }

            // ── currency ──────────────────────────────────────────────────────
            var currency = GetField(row, mapping.CurrencyColumn);
            if (!CurrencyRegex.IsMatch(currency))
                issues.Add(Error("currency", $"Currency '{currency}' must be a 3-letter ISO 4217 code (e.g. USD, EUR, PLN).", IssueCategory.Format));

            // ── transactionDate ───────────────────────────────────────────────
            var dateStr = GetField(row, mapping.TransactionDateColumn);
            if (!TryParseDate(dateStr, out var transactionDate))
            {
                issues.Add(Error("transactionDate", $"'{dateStr}' is not a recognisable date. Use YYYY-MM-DD.", IssueCategory.Format));
            }
            else if (transactionDate < MinDate)
            {
                issues.Add(Error("transactionDate", $"Transaction date '{transactionDate.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)}' is before the minimum date {MinDate.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)}.", IssueCategory.Format));
            }
            else if (transactionDate > today)
            {
                issues.Add(Error("transactionDate", $"Transaction date '{transactionDate.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)}' is in the future.", IssueCategory.Format));
            }

            // ── externalId (optional column) ──────────────────────────────────
            if (mapping.ExternalIdColumn is not null)
            {
                var externalId = GetField(row, mapping.ExternalIdColumn);
                if (!string.IsNullOrEmpty(externalId))
                {
                    if (existingExternalIds.Contains(externalId))
                    {
                        issues.Add(Warn("externalId", $"External ID '{externalId}' was already imported — this row will be skipped.", IssueCategory.Reference));
                    }
                    else if (fileExternalIds.Contains(externalId))
                    {
                        issues.Add(Warn("externalId", $"External ID '{externalId}' appears more than once in this file — duplicate rows will be skipped.", IssueCategory.Reference));
                    }
                    else
                    {
                        fileExternalIds.Add(externalId);
                    }
                }
            }

            results.Add(new TransactionRowValidationResult
            {
                RowNumber = rowNum,
                OriginalData = row,
                Issues = issues,
            });
        }

        return results;
    }

    private static string GetField(Dictionary<string, string> row, string column) =>
        row.TryGetValue(column, out var val) ? val.Trim() : string.Empty;

    private static bool TryParseDate(string s, out DateOnly result)
    {
        foreach (var fmt in DateFormats)
        {
            if (DateOnly.TryParseExact(s, fmt, System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out result))
                return true;
        }
        result = default;
        return false;
    }

    private static ValidationIssue Error(string field, string msg, IssueCategory cat = IssueCategory.Other) =>
        new() { Field = field, Message = msg, Severity = IssueSeverity.Error, Category = cat };

    private static ValidationIssue Warn(string field, string msg, IssueCategory cat = IssueCategory.Other) =>
        new() { Field = field, Message = msg, Severity = IssueSeverity.Warning, Category = cat };
}
