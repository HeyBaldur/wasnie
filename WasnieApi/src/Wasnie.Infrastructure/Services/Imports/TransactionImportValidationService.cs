using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Common.Abstractions;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Models.Imports;
using Wasnie.Application.Services.Imports;
using Wasnie.Domain.Compensation.Enums;

namespace Wasnie.Infrastructure.Services.Imports;

public sealed class TransactionImportValidationService(IApplicationDbContext db, IClock clock)
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
        // Pre-load all data up front to avoid N+1 queries.
        var payeesByCode = await db.Payees
            .ToDictionaryAsync(p => p.EmployeeCode, p => p.Id, StringComparer.OrdinalIgnoreCase, ct);

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
                issues.Add(Error("referenceNumber", "Reference number is required."));
            }
            else if (fileReferenceNumbers.Contains(referenceNumber))
            {
                issues.Add(Error("referenceNumber", "Reference number already appears in this file."));
            }
            else if (existingReferenceNumbers.Contains(referenceNumber))
            {
                issues.Add(Error("referenceNumber", "Reference number already exists."));
            }
            else
            {
                fileReferenceNumbers.Add(referenceNumber);
            }

            // ── payeeCode ─────────────────────────────────────────────────────
            var payeeCode = GetField(row, mapping.PayeeCodeColumn);
            if (string.IsNullOrWhiteSpace(payeeCode))
                issues.Add(Error("payeeCode", "Payee code is required."));
            else if (!payeesByCode.ContainsKey(payeeCode))
                issues.Add(Error("payeeCode", "Payee code not found."));

            // ── amount ────────────────────────────────────────────────────────
            var amountStr = GetField(row, mapping.AmountColumn);
            if (!decimal.TryParse(amountStr, System.Globalization.NumberStyles.Number,
                    System.Globalization.CultureInfo.InvariantCulture, out var amount))
            {
                issues.Add(Error("amount", "Amount must be a number."));
            }
            else if (amount <= 0)
            {
                issues.Add(Error("amount", "Amount must be greater than zero."));
            }

            // ── currency ──────────────────────────────────────────────────────
            var currency = GetField(row, mapping.CurrencyColumn);
            if (!CurrencyRegex.IsMatch(currency))
                issues.Add(Error("currency", "Currency must be a 3-letter ISO 4217 code."));

            // ── transactionDate ───────────────────────────────────────────────
            var dateStr = GetField(row, mapping.TransactionDateColumn);
            if (!TryParseDate(dateStr, out var transactionDate))
            {
                issues.Add(Error("transactionDate", "Transaction date is not a recognisable date. Use YYYY-MM-DD."));
            }
            else if (transactionDate < MinDate)
            {
                issues.Add(Error("transactionDate", "Transaction date cannot be before 2000-01-01."));
            }
            else if (transactionDate > today)
            {
                issues.Add(Error("transactionDate", "Transaction date cannot be in the future."));
            }

            // ── externalId (optional column) ──────────────────────────────────
            if (mapping.ExternalIdColumn is not null)
            {
                var externalId = GetField(row, mapping.ExternalIdColumn);
                if (!string.IsNullOrEmpty(externalId))
                {
                    if (existingExternalIds.Contains(externalId))
                    {
                        issues.Add(Warn("externalId", "External ID already imported — this row will be skipped."));
                    }
                    else if (fileExternalIds.Contains(externalId))
                    {
                        issues.Add(Warn("externalId", "External ID appears more than once in this file — duplicate rows will be skipped."));
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
            if (DateOnly.TryParseExact(s, fmt, null, System.Globalization.DateTimeStyles.None, out result))
                return true;
        }
        result = default;
        return false;
    }

    private static ValidationIssue Error(string field, string msg) =>
        new() { Field = field, Message = msg, Severity = IssueSeverity.Error };

    private static ValidationIssue Warn(string field, string msg) =>
        new() { Field = field, Message = msg, Severity = IssueSeverity.Warning };
}
