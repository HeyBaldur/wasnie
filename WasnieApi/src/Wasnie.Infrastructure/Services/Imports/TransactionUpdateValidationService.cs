using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Models.Imports;
using Wasnie.Application.Services.Imports;
using Wasnie.Domain.Compensation.Enums;
using Wasnie.Infrastructure.Persistence;

namespace Wasnie.Infrastructure.Services.Imports;

public sealed class TransactionUpdateValidationService(ApplicationDbContext db)
    : ITransactionUpdateValidationService
{

    public async Task<List<TransactionUpdateRowPreviewResult>> ValidateAsync(
        List<Dictionary<string, string>> rows,
        TransactionUpdateColumnMapping mapping,
        CancellationToken ct = default)
    {
        // Pre-load all existing transactions by ReferenceNumber for this tenant.
        // Batch lookup avoids N+1 per row.
        var refNumbers = rows
            .Select(r => GetField(r, mapping.ReferenceNumberColumn))
            .Where(r => !string.IsNullOrWhiteSpace(r))
            .Distinct()
            .ToList();

        var existingByRef = await db.CompensationTransactions
            .Where(t => refNumbers.Contains(t.ReferenceNumber))
            .Select(t => new ExistingTxProjection(
                t.Id, t.ReferenceNumber, t.Amount.Amount, t.Amount.Currency,
                t.TransactionDate, t.PayeeId, t.Status))
            .ToDictionaryAsync(t => t.ReferenceNumber, StringComparer.OrdinalIgnoreCase, ct);

        // Pre-load payee codes for payee lookup and reverse-lookup (id → code for diff display).
        var payeesByCode = mapping.PayeeCodeColumn is not null
            ? await db.Payees.ToDictionaryAsync(
                p => p.EmployeeCode, p => p.Id,
                StringComparer.OrdinalIgnoreCase, ct)
            : new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);

        // Inverted lookup for showing the existing payee's code in diffs.
        var payeeIdToCode = payeesByCode.ToDictionary(kvp => kvp.Value, kvp => kvp.Key);

        var results = new List<TransactionUpdateRowPreviewResult>(rows.Count);

        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            var rowNum = i + 1;
            var refNum = GetField(row, mapping.ReferenceNumberColumn);
            var issues = new List<ValidationIssue>();
            var diffs = new List<FieldDiff>();

            // ReferenceNumber must be present.
            if (string.IsNullOrWhiteSpace(refNum))
            {
                issues.Add(new ValidationIssue
                {
                    Field = "ReferenceNumber",
                    Message = "ReferenceNumber is required — it is the key used to locate the existing record.",
                    Severity = IssueSeverity.Error,
                    Category = IssueCategory.Required,
                });
                results.Add(ErrorResult(rowNum, row, issues));
                continue;
            }

            if (!existingByRef.TryGetValue(refNum, out var existing))
            {
                issues.Add(new ValidationIssue
                {
                    Field = "ReferenceNumber",
                    Message = $"ReferenceNumber '{refNum}' not found in this tenant.",
                    Severity = IssueSeverity.Error,
                    Category = IssueCategory.Reference,
                });
                results.Add(ErrorResult(rowNum, row, issues));
                continue;
            }

            // Paid transactions are blocked.
            if (existing.Status == CompensationTransactionStatus.Paid)
            {
                issues.Add(new ValidationIssue
                {
                    Field = "Status",
                    Message = $"Transaction '{refNum}' is Paid. Cannot update a transaction whose payout is already paid. Use cancellation + correction flow instead.",
                    Severity = IssueSeverity.Error,
                    Category = IssueCategory.Other,
                });
                results.Add(ErrorResult(rowNum, row, issues));
                continue;
            }

            // Compute diffs for mapped columns.
            if (mapping.AmountColumn is not null)
            {
                var amountStr = GetField(row, mapping.AmountColumn);
                if (!string.IsNullOrWhiteSpace(amountStr) &&
                    decimal.TryParse(amountStr, NumberStyles.Number, CultureInfo.InvariantCulture, out var newAmount) &&
                    newAmount != existing.Amount)
                {
                    diffs.Add(new FieldDiff
                    {
                        FieldName = "Amount",
                        OldValue = existing.Amount.ToString(CultureInfo.InvariantCulture),
                        NewValue = newAmount.ToString(CultureInfo.InvariantCulture),
                    });
                }
            }

            if (mapping.CurrencyColumn is not null)
            {
                var newCurrency = GetField(row, mapping.CurrencyColumn).Trim().ToUpperInvariant();
                if (!string.IsNullOrWhiteSpace(newCurrency) &&
                    !string.Equals(newCurrency, existing.Currency, StringComparison.OrdinalIgnoreCase))
                {
                    diffs.Add(new FieldDiff
                    {
                        FieldName = "Currency",
                        OldValue = existing.Currency,
                        NewValue = newCurrency,
                    });
                }
            }

            if (mapping.TransactionDateColumn is not null)
            {
                var dateStr = GetField(row, mapping.TransactionDateColumn);
                if (!string.IsNullOrWhiteSpace(dateStr) && TryParseDate(dateStr, out var newDate) &&
                    newDate != existing.TransactionDate)
                {
                    diffs.Add(new FieldDiff
                    {
                        FieldName = "TransactionDate",
                        OldValue = existing.TransactionDate.ToString("yyyy-MM-dd"),
                        NewValue = newDate.ToString("yyyy-MM-dd"),
                    });
                }
            }

            if (mapping.PayeeCodeColumn is not null)
            {
                var newCode = GetField(row, mapping.PayeeCodeColumn).Trim();
                if (!string.IsNullOrWhiteSpace(newCode))
                {
                    if (!payeesByCode.TryGetValue(newCode, out var newPayeeId))
                    {
                        issues.Add(new ValidationIssue
                        {
                            Field = "StaffId",
                            Message = $"Payee code '{newCode}' not found. Create the payee first or correct the code in your file.",
                            Severity = IssueSeverity.Error,
                            Category = IssueCategory.Reference,
                        });
                    }
                    else if (newPayeeId != existing.PayeeId)
                    {
                        var oldCode = existing.PayeeId.HasValue
                            ? payeeIdToCode.GetValueOrDefault(existing.PayeeId.Value, "Unassigned")
                            : "Unassigned";
                        diffs.Add(new FieldDiff
                        {
                            FieldName = "StaffId",
                            OldValue = oldCode,
                            NewValue = newCode,
                        });
                    }
                }
            }

            if (issues.Exists(iss => iss.Severity == IssueSeverity.Error))
            {
                results.Add(ErrorResult(rowNum, row, issues, existing.Id));
                continue;
            }

            var status = diffs.Count > 0 ? UpdateRowStatus.WillUpdate : UpdateRowStatus.NoChanges;
            results.Add(new TransactionUpdateRowPreviewResult
            {
                RowNumber = rowNum,
                OriginalData = row,
                Status = status,
                Diffs = diffs,
                Issues = issues,
                ExistingTransactionId = existing.Id,
            });
        }

        return results;
    }

    private static TransactionUpdateRowPreviewResult ErrorResult(
        int rowNum, Dictionary<string, string> row, List<ValidationIssue> issues, Guid? txId = null) =>
        new()
        {
            RowNumber = rowNum,
            OriginalData = row,
            Status = UpdateRowStatus.Error,
            Diffs = [],
            Issues = issues,
            ExistingTransactionId = txId,
        };

    private static string GetField(Dictionary<string, string> row, string? column) =>
        column is not null && row.TryGetValue(column, out var val) ? val.Trim() : string.Empty;

    private static bool TryParseDate(string s, out DateOnly result) =>
        DateOnly.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out result);

    private sealed record ExistingTxProjection(
        Guid Id, string ReferenceNumber, decimal Amount, string Currency,
        DateOnly TransactionDate, Guid? PayeeId, CompensationTransactionStatus Status);
}
