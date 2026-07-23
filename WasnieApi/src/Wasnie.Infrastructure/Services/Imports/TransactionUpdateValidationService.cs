using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Common.Abstractions;
using Wasnie.Application.Models.Imports;
using Wasnie.Application.Services.Imports;
using Wasnie.Domain.Compensation.Enums;
using Wasnie.Domain.Compensation.Transactions;
using Wasnie.Infrastructure.Persistence;

namespace Wasnie.Infrastructure.Services.Imports;

public sealed class TransactionUpdateValidationService(ApplicationDbContext db, IClock clock)
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
                t.Quantity, t.TransactionDate, t.PayeeId, t.Status, t.Description))
            .ToDictionaryAsync(t => t.ReferenceNumber, StringComparer.OrdinalIgnoreCase, ct);

        // Pre-load payees — include IsActive for inactive warning (mirrors IMPORT behavior).
        var payeesByCode = mapping.PayeeCodeColumn is not null
            ? await db.Payees
                .Select(p => new { p.EmployeeCode, p.Id, p.IsActive })
                .ToDictionaryAsync(
                    p => p.EmployeeCode,
                    p => (p.Id, p.IsActive),
                    StringComparer.OrdinalIgnoreCase,
                    ct)
            : new Dictionary<string, (Guid Id, bool IsActive)>(StringComparer.OrdinalIgnoreCase);

        // Inverted lookup for showing the existing payee's code in diffs.
        var payeeIdToCode = payeesByCode.ToDictionary(kvp => kvp.Value.Id, kvp => kvp.Key);

        var today = DateOnly.FromDateTime(clock.UtcNow);
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
            // For each editable field: if the column is mapped and the cell is non-blank,
            // validate the new value using the same rules as the IMPORT wizard (shared helper),
            // then compute a diff only when validation passes and the value actually changed.

            if (mapping.AmountColumn is not null)
            {
                var amountStr = GetField(row, mapping.AmountColumn);
                if (!string.IsNullOrWhiteSpace(amountStr))
                {
                    var amountIssue = TransactionFieldValidators.ValidateAmount(amountStr, out var newAmount);
                    if (amountIssue is not null)
                        issues.Add(amountIssue);
                    else if (newAmount != existing.Amount)
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
                if (!string.IsNullOrWhiteSpace(newCurrency))
                {
                    var currencyIssue = TransactionFieldValidators.ValidateCurrency(newCurrency);
                    if (currencyIssue is not null)
                        issues.Add(currencyIssue);
                    else if (!string.Equals(newCurrency, existing.Currency, StringComparison.OrdinalIgnoreCase))
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
                if (!string.IsNullOrWhiteSpace(dateStr))
                {
                    var dateIssue = TransactionFieldValidators.ValidateTransactionDate(dateStr, today, out var newDate);
                    if (dateIssue is not null)
                        issues.Add(dateIssue);
                    else if (newDate != existing.TransactionDate)
                        diffs.Add(new FieldDiff
                        {
                            FieldName = "TransactionDate",
                            OldValue = existing.TransactionDate.ToString("yyyy-MM-dd"),
                            NewValue = newDate.ToString("yyyy-MM-dd"),
                        });
                }
            }

            if (mapping.QuantityColumn is not null)
            {
                var quantityStr = GetField(row, mapping.QuantityColumn);
                if (!string.IsNullOrWhiteSpace(quantityStr))
                {
                    var quantityIssue = TransactionFieldValidators.ValidateQuantity(quantityStr, out var newQty);
                    if (quantityIssue is not null)
                        issues.Add(quantityIssue);
                    else if (newQty != existing.Quantity)
                        diffs.Add(new FieldDiff
                        {
                            FieldName = "Quantity",
                            OldValue = existing.Quantity.ToString(CultureInfo.InvariantCulture),
                            NewValue = newQty.ToString(CultureInfo.InvariantCulture),
                        });
                }
            }

            // Description: a label, never a money value. It can never make a row invalid — the only
            // issue it can raise is the truncation Warning. Blank cell = "no change", same rule as
            // every other column here (so a re-upload cannot blank out an existing description).
            if (mapping.DescriptionColumn is not null)
            {
                var descriptionStr = GetField(row, mapping.DescriptionColumn);
                if (!string.IsNullOrWhiteSpace(descriptionStr))
                {
                    var descriptionIssue = TransactionFieldValidators.ValidateDescription(descriptionStr);
                    if (descriptionIssue is not null)
                        issues.Add(descriptionIssue);

                    // Same normalization the domain will apply, so the previewed value is the stored value.
                    var newDescription = CompensationTransaction.NormalizeDescription(descriptionStr);
                    if (!string.Equals(newDescription, existing.Description, StringComparison.Ordinal))
                        diffs.Add(new FieldDiff
                        {
                            FieldName = "Description",
                            OldValue = existing.Description ?? string.Empty,
                            NewValue = newDescription,
                        });
                }
            }

            if (mapping.PayeeCodeColumn is not null)
            {
                var newCode = GetField(row, mapping.PayeeCodeColumn).Trim();
                if (!string.IsNullOrWhiteSpace(newCode))
                {
                    if (!payeesByCode.TryGetValue(newCode, out var payeeMatch))
                    {
                        issues.Add(new ValidationIssue
                        {
                            Field = "StaffId",
                            Message = $"Payee code '{newCode}' not found. Create the payee first or correct the code in your file.",
                            Severity = IssueSeverity.Error,
                            Category = IssueCategory.Reference,
                        });
                    }
                    else
                    {
                        // Inactive payee → Warning (mirrors IMPORT Decision 12: row still processable).
                        if (!payeeMatch.IsActive)
                            issues.Add(new ValidationIssue
                            {
                                Field = "StaffId",
                                Message = $"Payee '{newCode}' is inactive — assignment will be historical.",
                                Severity = IssueSeverity.Warning,
                                Category = IssueCategory.Reference,
                            });

                        if (payeeMatch.Id != existing.PayeeId)
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

    private sealed record ExistingTxProjection(
        Guid Id, string ReferenceNumber, decimal Amount, string Currency,
        int Quantity, DateOnly TransactionDate, Guid? PayeeId, CompensationTransactionStatus Status,
        string? Description);
}
