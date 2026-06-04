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

        // Pre-load plan currencies for currency-vs-plan validation (WI-PROD-T-FIX-3).
        // Step 1: collect payee IDs referenced in this batch.
        var payeeIdsInBatch = new HashSet<Guid>();
        foreach (var row in rows)
        {
            var code = GetField(row, mapping.PayeeCodeColumn);
            if (!string.IsNullOrWhiteSpace(code) && payeesByCode.TryGetValue(code, out var p))
                payeeIdsInBatch.Add(p.Id);
        }

        // Step 2: load all active assignments for those payees.
        // Load full entities in-memory — EF Core does not reliably translate DateOnly
        // owned-type comparisons (EffectivePeriod.Start/End) in SQL WHERE clauses.
        var activeAssignments = payeeIdsInBatch.Count > 0
            ? await db.PlanAssignments
                .Where(pa => pa.Status == AssignmentStatus.Active
                          && payeeIdsInBatch.Contains(pa.PayeeId))
                .ToListAsync(ct)
            : [];

        // Step 3: load plan Currency+Name for all referenced plans (one query).
        var planIds = activeAssignments.Select(pa => pa.PlanId).ToHashSet();
        var planInfoById = planIds.Count > 0
            ? (await db.CompensationPlans
                .Where(p => planIds.Contains(p.Id))
                .Select(p => new { p.Id, p.Currency, p.Name })
                .ToListAsync(ct))
                .ToDictionary(p => p.Id, p => (p.Currency, p.Name))
            : new Dictionary<Guid, (string Currency, string Name)>();

        // Step 4: build PayeeId → active-assignments lookup for O(1) per-row access.
        var assignmentsByPayee = activeAssignments
            .GroupBy(pa => pa.PayeeId)
            .ToDictionary(g => g.Key, g => g.ToList());

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
                // Blank payeeCode: error if required; warning if Optional (Decision D + Decision #53).
                if (payeeIdRequired)
                    issues.Add(Error("payeeCode", "Payee code is required. Add it to your file or set Payee field to Optional in Settings.", IssueCategory.Required));
                else
                    issues.Add(Warn("payeeCode",
                        "No Staff ID provided — this transaction will be imported as Unassigned and requires manual assignment for commission calculation.",
                        IssueCategory.Required));
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
            var amountIssue = TransactionFieldValidators.ValidateAmount(amountStr, out _);
            if (amountIssue is not null) issues.Add(amountIssue);

            // ── quantity (optional column, defaults to 1 if not mapped or blank) ───────
            if (mapping.QuantityColumn is not null)
            {
                var quantityStr = GetField(row, mapping.QuantityColumn);
                var quantityIssue = TransactionFieldValidators.ValidateQuantity(quantityStr, out _);
                if (quantityIssue is not null) issues.Add(quantityIssue);
            }

            // ── currency ──────────────────────────────────────────────────────
            var currency = GetField(row, mapping.CurrencyColumn);
            var currencyIssue = TransactionFieldValidators.ValidateCurrency(currency);
            if (currencyIssue is not null) issues.Add(currencyIssue);

            // ── transactionDate ───────────────────────────────────────────────
            var dateStr = GetField(row, mapping.TransactionDateColumn);
            var dateIssue = TransactionFieldValidators.ValidateTransactionDate(dateStr, today, out _);
            if (dateIssue is not null) issues.Add(dateIssue);

            // ── plan currency check (Pattern B) ──────────────────────────────
            // Pattern B: currency mismatch is a routing condition, not an error.
            // If payee has ANY plan in the transaction's currency covering the date → fine, routes there.
            // If payee has plans covering the date but NONE in the transaction's currency → informational
            // warning: the transaction will remain Pending until a matching-currency plan is assigned.
            if (!string.IsNullOrWhiteSpace(payeeCode)
                && payeesByCode.TryGetValue(payeeCode, out var resolvedPayee)
                && TransactionFieldValidators.ValidateCurrency(currency) is null
                && TransactionFieldValidators.TryParseDate(dateStr, out var txDateForCurrencyCheck)
                && assignmentsByPayee.TryGetValue(resolvedPayee.Id, out var payeeAssignments))
            {
                var assignmentsForDate = payeeAssignments.Where(pa =>
                    pa.EffectivePeriod is not null &&
                    pa.EffectivePeriod.Start <= txDateForCurrencyCheck &&
                    pa.EffectivePeriod.End >= txDateForCurrencyCheck).ToList();

                if (assignmentsForDate.Count > 0)
                {
                    var hasCurrencyMatch = assignmentsForDate.Any(pa =>
                        planInfoById.TryGetValue(pa.PlanId, out var pi) &&
                        string.Equals(pi.Currency, currency, StringComparison.OrdinalIgnoreCase));

                    if (!hasCurrencyMatch)
                    {
                        // No plan in this currency covers the payee's date — informational, not an error.
                        issues.Add(Warning("currency",
                            $"Payee '{payeeCode}' has no plan in {currency} covering this date. " +
                            $"Transaction will remain Pending until a {currency} plan is assigned.",
                            IssueCategory.Reference));
                    }
                }
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

    private static ValidationIssue Error(string field, string msg, IssueCategory cat = IssueCategory.Other) =>
        new() { Field = field, Message = msg, Severity = IssueSeverity.Error, Category = cat };

    private static ValidationIssue Warning(string field, string msg, IssueCategory cat = IssueCategory.Other) =>
        new() { Field = field, Message = msg, Severity = IssueSeverity.Warning, Category = cat };

    private static ValidationIssue Warn(string field, string msg, IssueCategory cat = IssueCategory.Other) =>
        new() { Field = field, Message = msg, Severity = IssueSeverity.Warning, Category = cat };
}
