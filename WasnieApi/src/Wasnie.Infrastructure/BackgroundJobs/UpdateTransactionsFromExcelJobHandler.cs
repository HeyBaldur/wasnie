using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Wasnie.Application.BackgroundJobs;
using Wasnie.Application.Common.Abstractions;
using Wasnie.Application.Common.Models;
using Wasnie.Application.Models.Imports;
using Wasnie.Domain.Audit;
using Wasnie.Domain.Compensation.Transactions;
using Wasnie.Domain.Compensation.ValueObjects;
using Wasnie.Domain.Exceptions;
using Wasnie.Infrastructure.Persistence;

namespace Wasnie.Infrastructure.BackgroundJobs;

public sealed class UpdateTransactionsFromExcelJobHandler(
    ApplicationDbContext db,
    IClock clock,
    IGuidGenerator guid,
    ILogger<UpdateTransactionsFromExcelJobHandler> logger)
    : JobHandlerBase<TransactionUpdatePayload>
{
    private const int ChunkSize = 50;

    private static readonly string[] DateFormats =
        ["yyyy-MM-dd", "MM/dd/yyyy", "dd/MM/yyyy", "M/d/yyyy", "d/M/yyyy", "yyyy/MM/dd"];

    public override async Task HandleAsync(
        TransactionUpdatePayload payload,
        JobContext context,
        CancellationToken ct)
    {
        var startedAt = clock.UtcNowOffset;

        logger.LogInformation(
            "UpdateTransactionsFromExcelJob {JobId}: starting update of '{FileName}' " +
            "for tenant {TenantId}. Rows: {RowCount}",
            context.JobId, payload.OriginalFileName, payload.TenantId, payload.Rows.Count);

        // Batch-load existing transactions by ReferenceNumber (all rows in one query).
        var refNumbers = payload.Rows
            .Select(r => GetField(r, payload.ColumnMapping.ReferenceNumberColumn))
            .Where(r => !string.IsNullOrWhiteSpace(r))
            .Distinct()
            .ToList();

        var existingByRef = await db.CompensationTransactions
            .Where(t => refNumbers.Contains(t.ReferenceNumber))
            .ToDictionaryAsync(t => t.ReferenceNumber, StringComparer.OrdinalIgnoreCase, ct);

        // Batch-load payee codes.
        var payeesByCode = payload.ColumnMapping.PayeeCodeColumn is not null
            ? await db.Payees.ToDictionaryAsync(
                p => p.EmployeeCode, p => p.Id, StringComparer.OrdinalIgnoreCase, ct)
            : new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);

        var totalRows = payload.Rows.Count;
        var updatedCount = 0;
        var skippedNoChanges = 0;
        var skippedErrors = 0;
        var processedSoFar = 0;

        // Process in chunks of 50, each in its own DB transaction.
        var chunks = payload.Rows
            .Select((row, idx) => (row, chunkIdx: idx / ChunkSize))
            .GroupBy(x => x.chunkIdx, x => x.row)
            .ToList();

        foreach (var chunk in chunks)
        {
            ct.ThrowIfCancellationRequested();

            var auditEntries = new List<AuditLog>();

            await using var sqlTx = await db.Database.BeginTransactionAsync(ct);

            foreach (var row in chunk)
            {
                var refNum = GetField(row, payload.ColumnMapping.ReferenceNumberColumn);
                if (string.IsNullOrWhiteSpace(refNum) || !existingByRef.TryGetValue(refNum, out var tx))
                {
                    skippedErrors++;
                    continue;
                }

                // Paid transactions: block at job level (should have been caught in validation).
                if (tx.Status == Wasnie.Domain.Compensation.Enums.CompensationTransactionStatus.Paid)
                {
                    skippedErrors++;
                    continue;
                }

                // Resolve new values.
                Money? newAmount = null;
                if (payload.ColumnMapping.AmountColumn is not null)
                {
                    var amountStr = GetField(row, payload.ColumnMapping.AmountColumn);
                    if (!string.IsNullOrWhiteSpace(amountStr) &&
                        decimal.TryParse(amountStr, NumberStyles.Number, CultureInfo.InvariantCulture, out var amt) &&
                        amt != tx.Amount.Amount)
                    {
                        newAmount = Money.Of(amt, tx.Amount.Currency);
                    }
                }

                if (payload.ColumnMapping.CurrencyColumn is not null)
                {
                    var newCurrency = GetField(row, payload.ColumnMapping.CurrencyColumn).Trim().ToUpperInvariant();
                    if (!string.IsNullOrWhiteSpace(newCurrency) &&
                        !string.Equals(newCurrency, tx.Amount.Currency, StringComparison.OrdinalIgnoreCase))
                    {
                        var baseAmount = newAmount?.Amount ?? tx.Amount.Amount;
                        newAmount = Money.Of(baseAmount, newCurrency);
                    }
                }

                DateOnly? newDate = null;
                if (payload.ColumnMapping.TransactionDateColumn is not null)
                {
                    var dateStr = GetField(row, payload.ColumnMapping.TransactionDateColumn);
                    if (!string.IsNullOrWhiteSpace(dateStr) && TryParseDate(dateStr, out var parsedDate) &&
                        parsedDate != tx.TransactionDate)
                    {
                        newDate = parsedDate;
                    }
                }

                Guid? newPayeeId = null;
                if (payload.ColumnMapping.PayeeCodeColumn is not null)
                {
                    var newCode = GetField(row, payload.ColumnMapping.PayeeCodeColumn).Trim();
                    if (!string.IsNullOrWhiteSpace(newCode) &&
                        payeesByCode.TryGetValue(newCode, out var resolvedId) &&
                        resolvedId != tx.PayeeId)
                    {
                        newPayeeId = resolvedId;
                    }
                }

                int? newQuantity = null;
                if (payload.ColumnMapping.QuantityColumn is not null)
                {
                    var qtyStr = GetField(row, payload.ColumnMapping.QuantityColumn).Trim();
                    if (!string.IsNullOrWhiteSpace(qtyStr) &&
                        int.TryParse(qtyStr, out var parsedQty) &&
                        parsedQty >= 1 && parsedQty != tx.Quantity)
                    {
                        newQuantity = parsedQty;
                    }
                }

                // Description is a label, not a value: blank cell = no change (so a re-upload can never
                // blank an existing name), and it is kept OUT of the money-change set below on purpose.
                string? newDescription = null;
                if (payload.ColumnMapping.DescriptionColumn is not null)
                {
                    var descriptionStr = GetField(row, payload.ColumnMapping.DescriptionColumn);
                    if (!string.IsNullOrWhiteSpace(descriptionStr))
                    {
                        var normalized = CompensationTransaction.NormalizeDescription(descriptionStr);
                        if (!string.Equals(normalized, tx.Description, StringComparison.Ordinal))
                            newDescription = normalized;
                    }
                }

                // Only these four feed the calculation, so only these trigger credit supersession and
                // the Calculated → Pending revert. A description-only edit must not invalidate money.
                var hasValueChange = newAmount is not null || newQuantity is not null
                                  || newDate is not null || newPayeeId is not null;

                // No changes — skip silently.
                if (!hasValueChange && newDescription is null)
                {
                    skippedNoChanges++;
                    continue;
                }

                // Capture before-state for audit.
                var beforeSnapshot = new
                {
                    Amount = tx.Amount.Amount,
                    Currency = tx.Amount.Currency,
                    Quantity = tx.Quantity,
                    TransactionDate = tx.TransactionDate.ToString("yyyy-MM-dd"),
                    PayeeId = tx.PayeeId,
                    Status = tx.Status.ToString(),
                    Description = tx.Description,
                };

                // If Calculated: supersede non-superseded Credits first (Decision #46 Case A pattern).
                if (hasValueChange &&
                    tx.Status == Wasnie.Domain.Compensation.Enums.CompensationTransactionStatus.Calculated)
                {
                    var credits = await db.Credits
                        .Where(c => c.TransactionId == tx.Id && c.SupersededAt == null)
                        .ToListAsync(ct);

                    var supersededReason =
                        $"Transaction edited via Excel re-upload by {payload.RequestedByUserId} " +
                        $"at {clock.UtcNowOffset:O}. Original Credit superseded due to value change.";

                    foreach (var credit in credits)
                        credit.Supersede(supersededReason, clock.UtcNowOffset, guid.NewGuid());
                }

                try
                {
                    if (hasValueChange)
                        tx.ApplyExcelUpdate(newAmount, newQuantity, newDate, newPayeeId, payload.RequestedByUserId, clock.UtcNowOffset);

                    // Separate call by design: same Paid guard, no status transition (see UpdateDescription).
                    if (newDescription is not null)
                        tx.UpdateDescription(newDescription, payload.RequestedByUserId, clock.UtcNowOffset);

                    await db.SaveChangesAsync(ct);

                    var afterSnapshot = new
                    {
                        Amount = tx.Amount.Amount,
                        Currency = tx.Amount.Currency,
                        Quantity = tx.Quantity,
                        TransactionDate = tx.TransactionDate.ToString("yyyy-MM-dd"),
                        PayeeId = tx.PayeeId,
                        Status = tx.Status.ToString(),
                        Description = tx.Description,
                    };

                    auditEntries.Add(AuditLog.Create(
                        tenantId: payload.TenantId,
                        timestampUtc: clock.UtcNow,
                        actorUserId: payload.RequestedByUserId,
                        actorEmail: payload.RequestedByEmail,
                        action: AuditActions.TransactionUpdatedViaExcel,
                        resourceType: "Transactions",
                        resourceId: tx.Id.ToString(),
                        resourceDisplayName: tx.ReferenceNumber,
                        beforeJson: JsonSerializer.Serialize(beforeSnapshot),
                        afterJson: JsonSerializer.Serialize(afterSnapshot)));

                    updatedCount++;
                }
                catch (DomainException domEx)
                {
                    logger.LogWarning(
                        "UpdateTransactionsFromExcelJob {JobId}: skipping transaction {TxId} ({RefNum}). Reason: {Reason}",
                        context.JobId, tx.Id, refNum, domEx.Message);
                    db.ChangeTracker.Clear();

                    // Reload remaining transactions for the chunk.
                    existingByRef = await db.CompensationTransactions
                        .Where(t => refNumbers.Contains(t.ReferenceNumber))
                        .ToDictionaryAsync(t => t.ReferenceNumber, StringComparer.OrdinalIgnoreCase, ct);

                    skippedErrors++;
                }
            }

            // Audit batch for this chunk (mandatory for money operations).
            foreach (var entry in auditEntries)
                db.AuditLogs.Add(entry);

            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (Exception auditEx)
            {
                logger.LogError(auditEx,
                    "UpdateTransactionsFromExcelJob {JobId}: CRITICAL — audit batch failed. " +
                    "Updates were committed but audit entries could not be saved. Hangfire will retry.",
                    context.JobId);
                throw;
            }

            await sqlTx.CommitAsync(ct);
            processedSoFar += chunk.Count();
            await context.ReportProgressAsync(processedSoFar, totalRows, ct);
        }

        var summaryPayload = new
        {
            Updated = updatedCount,
            SkippedNoChanges = skippedNoChanges,
            SkippedErrors = skippedErrors,
        };
        await context.SetResultSummaryAsync(JsonSerializer.Serialize(summaryPayload), ct);

        logger.LogInformation(
            "UpdateTransactionsFromExcelJob {JobId}: complete. Updated={Updated}, " +
            "SkippedNoChanges={SkippedNoChanges}, SkippedErrors={SkippedErrors}, Elapsed={Elapsed:N1}s",
            context.JobId, updatedCount, skippedNoChanges, skippedErrors,
            (clock.UtcNowOffset - startedAt).TotalSeconds);
    }

    private static string GetField(Dictionary<string, string> row, string? column) =>
        column is not null && row.TryGetValue(column, out var val) ? val.Trim() : string.Empty;

    private static bool TryParseDate(string s, out DateOnly result)
    {
        foreach (var fmt in DateFormats)
        {
            if (DateOnly.TryParseExact(s, fmt, CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out result))
                return true;
        }
        result = default;
        return false;
    }
}
