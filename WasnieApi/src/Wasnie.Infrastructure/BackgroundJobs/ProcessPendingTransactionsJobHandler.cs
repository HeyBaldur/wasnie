using System.Diagnostics;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Wasnie.Application.BackgroundJobs;
using Wasnie.Application.Common.Abstractions;
using Wasnie.Application.Common.Models;
using Wasnie.Application.Compensation.Calculation;
using Wasnie.Application.Models.Calculation;
using Wasnie.Domain.Audit;
using Wasnie.Domain.Compensation.Enums;
using Wasnie.Domain.Exceptions;
using Wasnie.Infrastructure.Persistence;

namespace Wasnie.Infrastructure.BackgroundJobs;

public sealed class ProcessPendingTransactionsJobHandler(
    ApplicationDbContext db,
    IClock clock,
    IGuidGenerator guid,
    ICreditAllocationService creditAllocationService,
    ILogger<ProcessPendingTransactionsJobHandler> logger)
    : JobHandlerBase<ProcessPendingTransactionsPayload>
{
    private const int ChunkSize = 50;
    private const int VolumeNoticeThreshold = 5000;

    public override async Task HandleAsync(
        ProcessPendingTransactionsPayload payload,
        JobContext context,
        CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var startedAt = clock.UtcNowOffset;

        logger.LogInformation(
            "ProcessPendingTransactionsJob {JobId}: starting. Scope={Scope}, ScopeId={ScopeId}, " +
            "Period={PeriodStart}–{PeriodEnd}, Tenant={TenantId}, TriggeredBy={TriggeredBy}",
            context.JobId, payload.Scope, payload.ScopeId,
            payload.PeriodStart, payload.PeriodEnd,
            payload.TenantId, payload.TriggeredBy);

        // Load candidate transaction IDs (Pending, PayeeId IS NOT NULL, scoped per enum).
        var candidateIds = await LoadCandidateIdsAsync(payload, ct);

        if (candidateIds.Count == 0)
        {
            logger.LogInformation(
                "ProcessPendingTransactionsJob {JobId}: no candidates found. Done.", context.JobId);
            await context.SetResultSummaryAsync(JsonSerializer.Serialize(
                new { Processed = 0, CreditsCreated = 0, SkippedByOverlapRule = 0,
                      SkippedByIdempotency = 0, SkippedByValidation = 0,
                      SkipReasonCounts = new Dictionary<string, int>(),
                      SkipDetails = Array.Empty<object>() },
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }), ct);
            return;
        }

        if (candidateIds.Count >= VolumeNoticeThreshold)
        {
            logger.LogInformation(
                "ProcessPendingTransactionsJob {JobId}: large batch — {Count} candidates. " +
                "Processing with chunking.", context.JobId, candidateIds.Count);
        }

        // Skipping rule (Decision #54 / Decision #61 Case B):
        // Exclude transactions that already have non-superseded Credits (from any plan).
        var idsWithExistingCredits = await db.Credits
            .Where(c => c.SupersededAt == null && candidateIds.Contains(c.TransactionId))
            .Select(c => c.TransactionId)
            .Distinct()
            .ToListAsync(ct);

        var idsToSkipSet = new HashSet<Guid>(idsWithExistingCredits);
        var eligibleIds = candidateIds.Where(id => !idsToSkipSet.Contains(id)).ToList();
        var skippedByOverlapRule = idsToSkipSet.Count;

        logger.LogInformation(
            "ProcessPendingTransactionsJob {JobId}: {Total} candidates, {SkippedByOverlap} skipped " +
            "(existing Credits). Processing {Eligible} transactions.",
            context.JobId, candidateIds.Count, skippedByOverlapRule, eligibleIds.Count);

        // Pre-load payee name/code for skip log enrichment — two batched queries, not per-row.
        var txPayeeMap = await db.CompensationTransactions
            .Where(t => eligibleIds.Contains(t.Id) && t.PayeeId.HasValue)
            .Select(t => new { TxId = t.Id, PayeeId = (Guid)t.PayeeId! })
            .ToDictionaryAsync(x => x.TxId, x => x.PayeeId, ct);

        var payeeIds = txPayeeMap.Values.Distinct().ToList();
        var payeeById = payeeIds.Count > 0
            ? await db.Payees
                .Where(p => payeeIds.Contains(p.Id))
                .Select(p => new { p.Id, p.FullName, p.EmployeeCode })
                .ToDictionaryAsync(p => p.Id, p => (p.FullName, p.EmployeeCode), ct)
            : new Dictionary<Guid, (string FullName, string EmployeeCode)>();

        var totalToProcess = eligibleIds.Count;
        var processedSoFar = 0;
        var createdCreditCount = 0;
        var skippedByIdempotency = 0;
        var skipReasonCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var skipDetails = new List<(Guid TxId, string RefNum, DateOnly TxDate, decimal Amt, string Ccy, string? PayeeName, string? PayeeCode, string Reason)>();

        // Process in chunks; each chunk is its own DB transaction.
        var chunks = eligibleIds
            .Select((id, idx) => (id, chunkIdx: idx / ChunkSize))
            .GroupBy(x => x.chunkIdx, x => x.id)
            .ToList();

        foreach (var chunk in chunks)
        {
            // Honor cancellation at chunk boundary — already-committed chunks remain.
            ct.ThrowIfCancellationRequested();

            var chunkIds = chunk.ToList();
            var chunkSw = Stopwatch.StartNew();

            // Load transaction entities for this chunk (must be tracked for MarkCalculated).
            var transactions = await db.CompensationTransactions
                .Where(t => chunkIds.Contains(t.Id))
                .ToListAsync(ct);

            // Batch pre-load: all PlanAssignments for payees in this chunk (1 query, not N).
            // This avoids 2 DB roundtrips per transaction inside AllocateAsync.
            var chunkPayeeIds = transactions
                .Where(t => t.PayeeId.HasValue)
                .Select(t => t.PayeeId!.Value)
                .Distinct()
                .ToList();

            var chunkAssignments = chunkPayeeIds.Count > 0
                ? await db.PlanAssignments
                    .IgnoreQueryFilters()
                    .Where(a => a.TenantId == payload.TenantId && chunkPayeeIds.Contains(a.PayeeId))
                    .ToListAsync(ct)
                : [];

            var assignmentsByPayee = chunkAssignments
                .GroupBy(a => a.PayeeId)
                .ToDictionary(
                    g => g.Key,
                    g => (IReadOnlyList<Wasnie.Domain.Compensation.Assignments.PlanAssignment>)g.ToList());

            // Batch pre-load: all Plans+Rules for those assignments (1 query, not N).
            var chunkPlanIds = chunkAssignments.Select(a => a.PlanId).Distinct().ToList();
            var plansInChunk = chunkPlanIds.Count > 0
                ? await db.CompensationPlans
                    .IgnoreQueryFilters()
                    .Include(p => p.Rules)
                    .Where(p => p.TenantId == payload.TenantId && chunkPlanIds.Contains(p.Id))
                    .ToDictionaryAsync(p => p.Id, ct)
                : new Dictionary<Guid, Wasnie.Domain.Compensation.Plans.Plan>();

            logger.LogDebug(
                "ProcessPendingTransactionsJob {JobId}: chunk {ChunkSize} tx pre-loaded in {Ms}ms " +
                "({Assignments} assignments, {Plans} plans).",
                context.JobId, transactions.Count, chunkSw.ElapsedMilliseconds,
                chunkAssignments.Count, plansInChunk.Count);

            await using var sqlTx = await db.Database.BeginTransactionAsync(ct);

            foreach (var transaction in transactions)
            {
                // Re-check status inside the transaction (state may have changed since ID was loaded).
                if (transaction.Status != CompensationTransactionStatus.Pending) continue;

                try
                {
                    // Batch path: no DB queries inside AllocateAsync — uses pre-loaded lookups.
                    var credits = await creditAllocationService.AllocateAsync(
                        transaction, assignmentsByPayee, plansInChunk, ct);

                    foreach (var credit in credits)
                        db.Credits.Add(credit);

                    if (credits.Count > 0)
                    {
                        var total = credits.Skip(1).Aggregate(credits[0].CreditedAmount,
                            (acc, c) => acc.Add(c.CreditedAmount));
                        transaction.MarkCalculated(credits.Count, total, payload.TriggeredBy,
                            clock.UtcNowOffset, guid.NewGuid());
                        await db.SaveChangesAsync(ct);
                        createdCreditCount += credits.Count;
                    }
                }
                catch (DomainException domEx)
                {
                    // Per-transaction validation failure (e.g. currency mismatch) — skip and continue.
                    // The transaction remains Pending; the user will see it in skip details.
                    var reason = domEx.Message;
                    logger.LogWarning(
                        "ProcessPendingTransactionsJob {JobId}: skipping transaction {TxId}. Reason: {Reason}",
                        context.JobId, transaction.Id, reason);

                    (string FullName, string EmployeeCode) payeeInfo = default;
                    if (transaction.PayeeId.HasValue)
                        payeeById.TryGetValue(transaction.PayeeId.Value, out payeeInfo);

                    skipDetails.Add((
                        transaction.Id,
                        transaction.ReferenceNumber,
                        transaction.TransactionDate,
                        transaction.Amount.Amount,
                        transaction.Amount.Currency,
                        string.IsNullOrEmpty(payeeInfo.FullName) ? null : payeeInfo.FullName,
                        string.IsNullOrEmpty(payeeInfo.EmployeeCode) ? null : payeeInfo.EmployeeCode,
                        reason));
                    skipReasonCounts.TryGetValue(reason, out var existing);
                    skipReasonCounts[reason] = existing + 1;
                }
                catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
                {
                    logger.LogWarning(
                        "ProcessPendingTransactionsJob {JobId}: idempotency skip for transaction {TxId}.",
                        context.JobId, transaction.Id);
                    db.ChangeTracker.Clear();

                    // Reload transactions for the rest of this chunk since ChangeTracker was cleared.
                    transactions = await db.CompensationTransactions
                        .Where(t => chunkIds.Contains(t.Id))
                        .ToListAsync(ct);

                    skippedByIdempotency++;
                }
            }

            await sqlTx.CommitAsync(ct);
            processedSoFar += chunkIds.Count;
            await context.ReportProgressAsync(processedSoFar, totalToProcess, ct);

            logger.LogDebug(
                "ProcessPendingTransactionsJob {JobId}: chunk complete in {Ms}ms.",
                context.JobId, chunkSw.ElapsedMilliseconds);
        }

        var completedAt = clock.UtcNowOffset;
        var remaining = totalToProcess - processedSoFar;
        var totalValidationSkips = skipDetails.Count;

        logger.LogInformation(
            "ProcessPendingTransactionsJob {JobId}: complete. Processed={Processed}, " +
            "CreditsCreated={Credits}, SkippedByOverlap={SkippedOverlap}, " +
            "SkippedByValidation={SkippedValidation}, SkippedByIdempotency={SkippedIdempotency}, " +
            "Elapsed={Elapsed:N1}s",
            context.JobId, processedSoFar, createdCreditCount,
            skippedByOverlapRule, totalValidationSkips, skippedByIdempotency,
            (completedAt - startedAt).TotalSeconds);

        // Persist result summary so the UI can show skip details after polling.
        var summaryPayload = new
        {
            Processed = processedSoFar,
            CreditsCreated = createdCreditCount,
            SkippedByOverlapRule = skippedByOverlapRule,
            SkippedByIdempotency = skippedByIdempotency,
            SkippedByValidation = totalValidationSkips,
            SkipReasonCounts = skipReasonCounts,
            // First 200 skip entries for UI display (protection against unbounded JSON).
            SkipDetails = skipDetails.Take(200).Select(s => new {
                TxId = s.TxId,
                RefNum = s.RefNum,
                TxDate = s.TxDate.ToString("yyyy-MM-dd"),
                Amount = s.Amt,
                Currency = s.Ccy,
                PayeeName = s.PayeeName,
                PayeeCode = s.PayeeCode,
                Reason = s.Reason,
            }),
        };
        await context.SetResultSummaryAsync(
            JsonSerializer.Serialize(summaryPayload, new System.Text.Json.JsonSerializerOptions
            {
                PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
            }), ct);

        // Audit log for the run.
        var auditEntry = AuditLog.Create(
            tenantId: payload.TenantId,
            timestampUtc: clock.UtcNow,
            actorUserId: payload.TriggeredBy,
            actorEmail: payload.TriggeredByEmail,
            action: AuditActions.PendingTransactionsProcessed,
            resourceType: "Transactions",
            resourceId: context.JobId.ToString(),
            resourceDisplayName: $"ProcessPending:{payload.Scope}",
            beforeJson: null,
            afterJson: JsonSerializer.Serialize(new
            {
                JobId = context.JobId,
                payload.Scope,
                payload.ScopeId,
                PeriodStart = payload.PeriodStart?.ToString("yyyy-MM-dd"),
                PeriodEnd = payload.PeriodEnd?.ToString("yyyy-MM-dd"),
                TotalCandidates = candidateIds.Count,
                Processed = processedSoFar,
                CreditsCreated = createdCreditCount,
                SkippedByOverlapRule = skippedByOverlapRule,
                SkippedByValidation = totalValidationSkips,
                SkippedByIdempotency = skippedByIdempotency,
                Remaining = remaining,
                ElapsedSeconds = (completedAt - startedAt).TotalSeconds
            }));

        db.AuditLogs.Add(auditEntry);
        await db.SaveChangesAsync(ct);
    }

    private async Task<List<Guid>> LoadCandidateIdsAsync(ProcessPendingTransactionsPayload payload, CancellationToken ct)
    {
        return payload.Scope switch
        {
            ProcessPendingScope.ByPlanAssignment => await LoadByAssignmentAsync(payload, ct),
            ProcessPendingScope.ByPlan => await LoadByPlanAsync(payload, ct),
            ProcessPendingScope.ByPayeeAndPeriod => await LoadByPayeeAndPeriodAsync(payload, ct),
            _ => []
        };
    }

    private async Task<List<Guid>> LoadByAssignmentAsync(ProcessPendingTransactionsPayload payload, CancellationToken ct)
    {
        // Load full entity to avoid EF Core owned-type projection issues (DateRange is an owned type).
        var assignment = await db.PlanAssignments
            .IgnoreQueryFilters()
            .Where(a => a.Id == payload.ScopeId && a.TenantId == payload.TenantId)
            .FirstOrDefaultAsync(ct);

        if (assignment is null || assignment.EffectivePeriod is null) return [];

        var start = assignment.EffectivePeriod.Start;
        var end = assignment.EffectivePeriod.End;
        var payeeId = assignment.PayeeId;

        // Pattern B: only include transactions whose currency matches this plan's currency.
        var plan = await db.CompensationPlans
            .IgnoreQueryFilters()
            .Where(p => p.Id == assignment.PlanId && p.TenantId == payload.TenantId)
            .Select(p => new { p.Currency })
            .FirstOrDefaultAsync(ct);

        if (plan is null) return [];

        return await db.CompensationTransactions
            .Where(t => t.Status == CompensationTransactionStatus.Pending
                     && t.PayeeId == payeeId
                     && t.TransactionDate >= start
                     && t.TransactionDate <= end
                     && t.Amount.Currency == plan.Currency)
            .Select(t => t.Id)
            .ToListAsync(ct);
    }

    private async Task<List<Guid>> LoadByPlanAsync(ProcessPendingTransactionsPayload payload, CancellationToken ct)
    {
        // Pattern B: load plan currency — only transactions in this currency are eligible for this plan.
        var plan = await db.CompensationPlans
            .IgnoreQueryFilters()
            .Where(p => p.Id == payload.ScopeId && p.TenantId == payload.TenantId)
            .Select(p => new { p.Currency })
            .FirstOrDefaultAsync(ct);

        if (plan is null) return [];

        // Load full entities to avoid EF Core owned-type projection issues (DateRange is an owned type).
        var assignments = await db.PlanAssignments
            .IgnoreQueryFilters()
            .Where(a => a.PlanId == payload.ScopeId && a.TenantId == payload.TenantId
                     && a.Status == AssignmentStatus.Active)
            .ToListAsync(ct);

        var activeAssignments = assignments.Where(a => a.EffectivePeriod is not null).ToList();
        if (activeAssignments.Count == 0) return [];

        var planPayeeIds = activeAssignments.Select(a => a.PayeeId).Distinct().ToList();

        // Pattern B: filter by plan currency so payees with matching-date but different-currency
        // plans don't produce transactions that will be misrouted or skipped.
        var allPendingForPayees = await db.CompensationTransactions
            .Where(t => t.Status == CompensationTransactionStatus.Pending
                     && t.PayeeId.HasValue
                     && planPayeeIds.Contains(t.PayeeId!.Value)
                     && t.Amount.Currency == plan.Currency)
            .Select(t => new { t.Id, t.PayeeId, t.TransactionDate })
            .ToListAsync(ct);

        var ids = new List<Guid>();
        foreach (var a in activeAssignments)
        {
            var start = a.EffectivePeriod!.Start;
            var end = a.EffectivePeriod.End;
            ids.AddRange(allPendingForPayees
                .Where(t => t.PayeeId == a.PayeeId
                         && t.TransactionDate >= start
                         && t.TransactionDate <= end)
                .Select(t => t.Id));
        }
        return ids;
    }

    private async Task<List<Guid>> LoadByPayeeAndPeriodAsync(ProcessPendingTransactionsPayload payload, CancellationToken ct)
    {
        var payeeId = payload.ScopeId!.Value;
        return await db.CompensationTransactions
            .Where(t => t.Status == CompensationTransactionStatus.Pending
                     && t.PayeeId == payeeId
                     && (payload.PeriodStart == null || t.TransactionDate >= payload.PeriodStart.Value)
                     && (payload.PeriodEnd == null || t.TransactionDate <= payload.PeriodEnd.Value))
            .Select(t => t.Id)
            .ToListAsync(ct);
    }

    private static bool IsUniqueConstraintViolation(DbUpdateException ex) =>
        ex.InnerException is SqlException sqlEx && (sqlEx.Number == 2627 || sqlEx.Number == 2601);
}
