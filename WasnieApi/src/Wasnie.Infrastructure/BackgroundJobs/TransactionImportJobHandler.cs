using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Wasnie.Application.BackgroundJobs;
using Wasnie.Application.Common.Abstractions;
using Wasnie.Application.Common.Models;
using Wasnie.Application.Compensation.Calculation;
using Wasnie.Application.Models.Imports;
using Wasnie.Application.Services.Imports;
using Wasnie.Domain.Audit;
using Wasnie.Domain.Compensation.Enums;
using Wasnie.Domain.Compensation.Transactions;
using Wasnie.Domain.Compensation.ValueObjects;
using Wasnie.Domain.Entities;
using Wasnie.Domain.Exceptions;
using Wasnie.Infrastructure.Persistence;

namespace Wasnie.Infrastructure.BackgroundJobs;

public sealed class TransactionImportJobHandler(
    ApplicationDbContext db,
    IClock clock,
    IGuidGenerator guid,
    ITransactionImportValidationService validator,
    ICreditAllocationService creditAllocationService,
    ILogger<TransactionImportJobHandler> logger)
    : JobHandlerBase<TransactionImportPayload>
{
    private const int ChunkSize = 50;

    public override async Task HandleAsync(
        TransactionImportPayload payload,
        JobContext context,
        CancellationToken ct)
    {
        var startedAt = clock.UtcNowOffset;

        logger.LogInformation(
            "TransactionImportJob {JobId}: starting import of '{FileName}' for tenant {TenantId}. " +
            "Rows: {RowCount}. SkipWarnings: {SkipWarnings}",
            context.JobId, payload.OriginalFileName, payload.TenantId,
            payload.Rows.Count, payload.Options.SkipRowsWithWarnings);

        // Re-validate against current DB state — state may have changed since validate endpoint.
        var validationResults = await validator.ValidateAsync(payload.Rows, payload.ColumnMapping, ct);

        // Pre-load payee codes for chunk processing (tenant context is already set by dispatcher).
        var payeesByCode = await db.Payees
            .ToDictionaryAsync(p => p.EmployeeCode, p => p.Id, StringComparer.OrdinalIgnoreCase, ct);

        // Split rows into valid vs skipped.
        var validRows = new List<(int Index, Dictionary<string, string> Row, TransactionRowValidationResult Vr)>();
        var skippedCount = 0;

        for (var i = 0; i < payload.Rows.Count; i++)
        {
            var vr = validationResults[i];
            if (vr.HasErrors || (payload.Options.SkipRowsWithWarnings && vr.HasWarnings))
                skippedCount++;
            else
                validRows.Add((i, payload.Rows[i], vr));
        }

        var totalValidRows = validRows.Count;
        var createdTransactions = new List<CompensationTransaction>(totalValidRows);
        var processedSoFar = 0;
        var skippedByIdempotency = 0;
        var skippedByDomainValidation = 0;

        // Process in chunks of 50, each in its own transaction.
        var chunks = validRows
            .Select((item, idx) => (item, chunkIdx: idx / ChunkSize))
            .GroupBy(x => x.chunkIdx, x => x.item)
            .ToList();

        foreach (var chunk in chunks)
        {
            await using var sqlTx = await db.Database.BeginTransactionAsync(ct);

            foreach (var (_, row, _) in chunk)
            {
                var refNum = GetField(row, payload.ColumnMapping.ReferenceNumberColumn);
                var payeeCode = GetField(row, payload.ColumnMapping.PayeeCodeColumn);
                var amountStr = GetField(row, payload.ColumnMapping.AmountColumn);
                var currency = GetField(row, payload.ColumnMapping.CurrencyColumn);
                var dateStr = GetField(row, payload.ColumnMapping.TransactionDateColumn);
                var externalId = payload.ColumnMapping.ExternalIdColumn is not null
                    ? GetField(row, payload.ColumnMapping.ExternalIdColumn)
                    : null;

                // PayeeId is nullable (Decision D): blank payeeCode → null (row passed validation, Optional setting confirmed).
                // Inactive payee match → still assigned (historical assignment per Decision 12).
                Guid? payeeId = null;
                if (!string.IsNullOrWhiteSpace(payeeCode))
                {
                    if (!payeesByCode.TryGetValue(payeeCode, out var resolvedId))
                    {
                        // Payee not found — defensive skip (validation should have caught this).
                        skippedByIdempotency++;
                        continue;
                    }
                    payeeId = resolvedId;
                }

                if (!decimal.TryParse(amountStr, System.Globalization.NumberStyles.Number,
                        System.Globalization.CultureInfo.InvariantCulture, out var amount))
                {
                    skippedByIdempotency++;
                    continue;
                }

                if (!TryParseDate(dateStr, out var transactionDate))
                {
                    skippedByIdempotency++;
                    continue;
                }

                var money = Money.Of(amount, currency);
                var externalIdValue = string.IsNullOrEmpty(externalId) ? null : externalId;

                var quantityStr = payload.ColumnMapping.QuantityColumn is not null
                    ? GetField(row, payload.ColumnMapping.QuantityColumn)
                    : null;
                var quantity = 1;
                if (!string.IsNullOrWhiteSpace(quantityStr) &&
                    int.TryParse(quantityStr.Trim(), out var parsedQty) && parsedQty >= 1)
                    quantity = parsedQty;

                var transaction = CompensationTransaction.Ingest(
                    tenantId: payload.TenantId,
                    referenceNumber: refNum,
                    payeeId: payeeId,
                    amount: money,
                    transactionDate: transactionDate,
                    source: TransactionSource.EtlImport,
                    ingestedBy: payload.RequestedByUserId,
                    id: guid.NewGuid(),
                    now: clock.UtcNowOffset,
                    eventId: guid.NewGuid(),
                    externalId: externalIdValue,
                    quantity: quantity);

                db.CompensationTransactions.Add(transaction);

                try
                {
                    await db.SaveChangesAsync(ct);

                    // Allocate credits for this transaction (Decision #44: null payee = no credits).
                    var credits = await creditAllocationService.AllocateAsync(transaction, ct);
                    foreach (var credit in credits)
                        db.Credits.Add(credit);

                    if (credits.Count > 0)
                    {
                        var total = credits.Skip(1).Aggregate(credits[0].CreditedAmount,
                            (acc, c) => acc.Add(c.CreditedAmount));
                        transaction.MarkCalculated(credits.Count, total, payload.RequestedByUserId,
                            clock.UtcNowOffset, guid.NewGuid());
                        await db.SaveChangesAsync(ct);
                    }

                    createdTransactions.Add(transaction);
                }
                catch (DomainException domEx)
                {
                    // Per-row validation failure at import time (e.g. currency mismatch that slipped past
                    // Step 3 preview — belt-and-suspenders). Transaction was already saved as Pending;
                    // no credits allocated. Clear tracker so remaining rows in the chunk can proceed.
                    logger.LogWarning(
                        "TransactionImportJob {JobId}: skipping credit allocation for ref '{RefNum}' — {Reason}",
                        context.JobId, refNum, domEx.Message);
                    db.ChangeTracker.Clear();
                    skippedByDomainValidation++;
                }
                catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
                {
                    logger.LogWarning(
                        "TransactionImportJob {JobId}: idempotency skip for ref '{RefNum}' (unique constraint).",
                        context.JobId, refNum);

                    // Clear tracker so remaining rows in the chunk can proceed.
                    db.ChangeTracker.Clear();
                    skippedByIdempotency++;
                }
            }

            await sqlTx.CommitAsync(ct);
            processedSoFar += chunk.Count();
            await context.ReportProgressAsync(processedSoFar, totalValidRows, ct);
        }

        var completedAt = clock.UtcNowOffset;
        var totalCreated = createdTransactions.Count;
        var totalSkipped = skippedCount + skippedByIdempotency + skippedByDomainValidation;

        logger.LogInformation(
            "TransactionImportJob {JobId}: {Created} transactions created, {Skipped} skipped. " +
            "Writing audit records.",
            context.JobId, totalCreated, totalSkipped);

        // Build audit log entries for all created transactions.
        var auditEntries = createdTransactions.Select(t => AuditLog.Create(
            tenantId: payload.TenantId,
            timestampUtc: clock.UtcNow,
            actorUserId: payload.RequestedByUserId,
            actorEmail: payload.RequestedByEmail,
            action: AuditActions.TransactionIngested,
            resourceType: "Transactions",
            resourceId: t.Id.ToString(),
            resourceDisplayName: t.ReferenceNumber,
            beforeJson: null,
            afterJson: JsonSerializer.Serialize(new
            {
                t.Id,
                t.TenantId,
                t.ReferenceNumber,
                t.PayeeId,
                Amount = t.Amount.Amount,
                Currency = t.Amount.Currency,
                TransactionDate = t.TransactionDate.ToString("yyyy-MM-dd"),
                Source = t.Source.ToString(),
                t.ExternalId,
                t.IngestedBy,
                IngestedAt = t.IngestedAt.ToString("O"),
            }))).ToList();

        // Audit batch is MANDATORY for money audit — failure MUST throw.
        foreach (var entry in auditEntries)
            db.AuditLogs.Add(entry);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (Exception auditEx)
        {
            logger.LogError(auditEx,
                "TransactionImportJob {JobId}: CRITICAL — audit batch failed. " +
                "{Count} transactions were committed but audit entries could not be saved. " +
                "Hangfire will retry; idempotency will skip already-committed transactions.",
                context.JobId, totalCreated);
            throw; // Re-throw — Hangfire will retry; idempotency protects duplicate commit attempts.
        }

        // Write ImportAudit record.
        var auditStatus = totalSkipped == 0 ? "Success"
            : totalCreated == 0 ? "Failed"
            : "PartialSuccess";

        var importAudit = ImportAudit.Create(
            id: guid.NewGuid(),
            tenantId: payload.TenantId,
            importedBy: payload.RequestedByUserId,
            startedAt: startedAt,
            completedAt: completedAt,
            resourceType: "Transactions",
            totalRows: payload.Rows.Count,
            createdCount: totalCreated,
            skippedCount: totalSkipped,
            originalFileName: payload.OriginalFileName,
            status: auditStatus);

        db.ImportAudits.Add(importAudit);
        await db.SaveChangesAsync(ct);

        var elapsed = completedAt - startedAt;
        logger.LogInformation(
            "TransactionImportJob {JobId}: completed in {Elapsed:N1}s. " +
            "Created: {Created}, Skipped: {Skipped}, Status: {Status}.",
            context.JobId, elapsed.TotalSeconds, totalCreated, totalSkipped, auditStatus);
    }

    private static bool IsUniqueConstraintViolation(DbUpdateException ex)
    {
        return ex.InnerException is SqlException sqlEx
               && (sqlEx.Number == 2627 || sqlEx.Number == 2601);
    }

    private static string GetField(Dictionary<string, string> row, string column) =>
        row.TryGetValue(column, out var val) ? val.Trim() : string.Empty;

    private static bool TryParseDate(string s, out DateOnly result) =>
        DateOnly.TryParseExact(s, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out result);
}
