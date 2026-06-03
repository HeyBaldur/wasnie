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

        var totalToProcess = eligibleIds.Count;
        var processedSoFar = 0;
        var createdCreditCount = 0;
        var skippedByIdempotency = 0;

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

            // Load transaction entities for this chunk (must be tracked for MarkCalculated).
            var transactions = await db.CompensationTransactions
                .Where(t => chunkIds.Contains(t.Id))
                .ToListAsync(ct);

            await using var sqlTx = await db.Database.BeginTransactionAsync(ct);

            foreach (var transaction in transactions)
            {
                // Re-check status inside the transaction (state may have changed since ID was loaded).
                if (transaction.Status != CompensationTransactionStatus.Pending) continue;

                try
                {
                    var credits = await creditAllocationService.AllocateAsync(transaction, ct);
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
        }

        var completedAt = clock.UtcNowOffset;
        var remaining = totalToProcess - processedSoFar;

        logger.LogInformation(
            "ProcessPendingTransactionsJob {JobId}: complete. Processed={Processed}, " +
            "CreditsCreated={Credits}, SkippedByOverlap={SkippedOverlap}, " +
            "SkippedByIdempotency={SkippedIdempotency}, Elapsed={Elapsed:N1}s",
            context.JobId, processedSoFar, createdCreditCount,
            skippedByOverlapRule, skippedByIdempotency,
            (completedAt - startedAt).TotalSeconds);

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

        return await db.CompensationTransactions
            .Where(t => t.Status == CompensationTransactionStatus.Pending
                     && t.PayeeId == payeeId
                     && t.TransactionDate >= start
                     && t.TransactionDate <= end)
            .Select(t => t.Id)
            .ToListAsync(ct);
    }

    private async Task<List<Guid>> LoadByPlanAsync(ProcessPendingTransactionsPayload payload, CancellationToken ct)
    {
        // Load full entities to avoid EF Core owned-type projection issues (DateRange is an owned type).
        var assignments = await db.PlanAssignments
            .IgnoreQueryFilters()
            .Where(a => a.PlanId == payload.ScopeId && a.TenantId == payload.TenantId
                     && a.Status == AssignmentStatus.Active)
            .ToListAsync(ct);

        var ids = new List<Guid>();
        foreach (var a in assignments.Where(a => a.EffectivePeriod is not null))
        {
            var start = a.EffectivePeriod!.Start;
            var end = a.EffectivePeriod.End;
            var payeeId = a.PayeeId;

            var batch = await db.CompensationTransactions
                .Where(t => t.Status == CompensationTransactionStatus.Pending
                         && t.PayeeId == payeeId
                         && t.TransactionDate >= start
                         && t.TransactionDate <= end)
                .Select(t => t.Id)
                .ToListAsync(ct);

            ids.AddRange(batch);
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
