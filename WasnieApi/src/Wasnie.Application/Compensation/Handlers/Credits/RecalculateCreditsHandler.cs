using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Compensation.Calculation;
using Wasnie.Application.Compensation.Commands.Credits;
using Wasnie.Application.Compensation.Commands.PayRuns;
using Wasnie.Application.Models.Calculation;
using Wasnie.Domain.Authorization;
using Wasnie.Domain.Common.Results;
using Wasnie.Domain.Compensation.Enums;

namespace Wasnie.Application.Compensation.Handlers.Credits;

public sealed class RecalculateCreditsHandler(
    IApplicationDbContext db,
    ITenantContext tenantContext,
    ICurrentUserService currentUser,
    IAuthorizationService authorizationService,
    IBackgroundJobService backgroundJobService,
    ISender sender,
    ILogger<RecalculateCreditsHandler> logger)
    : IRequestHandler<RecalculateCreditsCommand, Result<RecalculateCreditsResult>>
{
    public async Task<Result<RecalculateCreditsResult>> Handle(
        RecalculateCreditsCommand request, CancellationToken cancellationToken)
    {
        await authorizationService.RequireAsync(Permission.CreditsRecalculate, cancellationToken);

        var tenantId = tenantContext.TenantId;
        var now = DateTimeOffset.UtcNow;
        var userId = currentUser.UserId ?? "system";

        // Load non-superseded, non-consumed credits whose transaction falls in the period.
        var transactionIdsInPeriod = await db.CompensationTransactions
            .Where(t => t.TenantId == tenantId
                     && t.TransactionDate >= request.PeriodStart
                     && t.TransactionDate <= request.PeriodEnd)
            .Select(t => t.Id)
            .ToListAsync(cancellationToken);

        if (transactionIdsInPeriod.Count == 0)
            return Result<RecalculateCreditsResult>.Success(new RecalculateCreditsResult(0, 0, []));

        var creditQuery = db.Credits
            .Where(c => c.TenantId == tenantId
                     && c.SupersededAt == null
                     && c.ConsumedAt == null
                     && transactionIdsInPeriod.Contains(c.TransactionId));

        if (request.PayeeId.HasValue)
            creditQuery = creditQuery.Where(c => c.PayeeId == request.PayeeId.Value);

        var credits = await creditQuery.ToListAsync(cancellationToken);

        if (credits.Count == 0)
            return Result<RecalculateCreditsResult>.Success(new RecalculateCreditsResult(0, 0, []));

        var affectedTransactionIds = credits.Select(c => c.TransactionId).Distinct().ToList();

        var transactions = await db.CompensationTransactions
            .Where(t => affectedTransactionIds.Contains(t.Id))
            .ToListAsync(cancellationToken);

        var processableTransactionIds = transactions
            .Where(t => t.Status != CompensationTransactionStatus.Paid
                     && t.Status != CompensationTransactionStatus.Cancelled)
            .Select(t => t.Id)
            .ToHashSet();

        int skippedPaidCount = transactions.Count - processableTransactionIds.Count;

        // Build affected payee set before touching anything — needed for pay run detection.
        var affectedPayeeIds = transactions
            .Where(t => processableTransactionIds.Contains(t.Id) && t.PayeeId.HasValue)
            .Select(t => t.PayeeId!.Value)
            .ToHashSet();

        // LÍNEA ROJA: detect pay runs covering affected payees in the period.
        // Block entirely if any are Approved or Paid. Draft runs are auto-deleted before
        // regenerating credits so they don't surface stale totals after recalculation.
        var draftRunIds = new List<Guid>();
        if (affectedPayeeIds.Count > 0)
        {
            var affectedPayeeIdsList = affectedPayeeIds.ToList();

            var payRunIdsForAffectedPayees = await db.CompensationPayouts
                .Where(p => p.TenantId == tenantId
                         && p.PayRunId.HasValue
                         && affectedPayeeIdsList.Contains(p.PayeeId))
                .Select(p => p.PayRunId!.Value)
                .Distinct()
                .ToListAsync(cancellationToken);

            if (payRunIdsForAffectedPayees.Count > 0)
            {
                var affectingPayRuns = await db.PayRuns
                    .Where(r => payRunIdsForAffectedPayees.Contains(r.Id)
                             && r.PeriodStart <= request.PeriodEnd
                             && r.PeriodEnd >= request.PeriodStart)
                    .Select(r => new { r.Id, r.Status, r.PeriodStart, r.PeriodEnd })
                    .ToListAsync(cancellationToken);

                var blockedRuns = affectingPayRuns
                    .Where(r => r.Status == PayRunStatus.Approved || r.Status == PayRunStatus.Paid)
                    .Select(r => new BlockingPayRunInfo(r.Id, r.Status.ToString(), r.PeriodStart, r.PeriodEnd))
                    .ToList();

                if (blockedRuns.Count > 0)
                {
                    logger.LogWarning(
                        "RecalculateCredits blocked: {Count} Approved/Paid pay run(s) cover period {Start}/{End}.",
                        blockedRuns.Count, request.PeriodStart, request.PeriodEnd);

                    return Result<RecalculateCreditsResult>.Success(
                        new RecalculateCreditsResult(0, skippedPaidCount, [], 0, blockedRuns));
                }

                draftRunIds = affectingPayRuns
                    .Where(r => r.Status == PayRunStatus.Draft)
                    .Select(r => r.Id)
                    .ToList();
            }
        }

        // Supersede credits for processable transactions only
        int supersededCount = 0;
        var supersededReason = $"Recalculate by {userId} at {now:yyyy-MM-ddTHH:mm:ssZ}";
        foreach (var credit in credits)
        {
            if (!processableTransactionIds.Contains(credit.TransactionId))
                continue;

            credit.Supersede(supersededReason, now, Guid.NewGuid());
            supersededCount++;
        }

        // Revert Calculated transactions to Pending
        foreach (var tx in transactions)
        {
            if (!processableTransactionIds.Contains(tx.Id))
                continue;

            if (tx.Status == CompensationTransactionStatus.Calculated)
                tx.RevertCalculatedToPending(userId, now);
        }

        await db.SaveChangesAsync(cancellationToken);

        // Auto-delete Draft pay runs via existing DeletePayRunDraftHandler (reuse, no duplication).
        // Deletion happens after credits are superseded and reverted — stale data is already gone
        // before the pay run disappears. Any deletion failure is logged but doesn't abort the flow.
        int deletedDraftCount = 0;
        foreach (var draftRunId in draftRunIds)
        {
            logger.LogInformation(
                "RecalculateCredits: auto-deleting Draft PayRun {PayRunId} (period {Start}/{End}).",
                draftRunId, request.PeriodStart, request.PeriodEnd);

            var deleteResult = await sender.Send(new DeletePayRunDraftCommand(draftRunId), cancellationToken);
            if (deleteResult.IsSuccess)
                deletedDraftCount++;
            else
                logger.LogWarning(
                    "RecalculateCredits: auto-delete of Draft PayRun {PayRunId} failed: {Error}",
                    draftRunId, deleteResult.Error);
        }

        // Enqueue one ProcessPending job per affected payee so chronological ordering runs fresh
        var jobIds = new List<Guid>();
        foreach (var payeeId in affectedPayeeIds)
        {
            var payload = new ProcessPendingTransactionsPayload(
                TenantId: tenantId,
                Scope: ProcessPendingScope.ByPayeeAndPeriod,
                ScopeId: payeeId,
                PeriodStart: request.PeriodStart,
                PeriodEnd: request.PeriodEnd,
                TriggeredBy: userId,
                TriggeredByEmail: currentUser.Email ?? string.Empty);

            var jobId = await backgroundJobService.EnqueueAsync(
                payload, tenantId, userId, currentUser.Email ?? string.Empty, cancellationToken);

            jobIds.Add(jobId);
        }

        request.AuditResourceId = $"Period:{request.PeriodStart:yyyy-MM-dd}/{request.PeriodEnd:yyyy-MM-dd}";

        return Result<RecalculateCreditsResult>.Success(
            new RecalculateCreditsResult(supersededCount, skippedPaidCount, jobIds, deletedDraftCount));
    }
}
