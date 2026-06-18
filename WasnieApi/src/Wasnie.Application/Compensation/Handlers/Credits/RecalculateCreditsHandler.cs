using MediatR;
using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Compensation.Calculation;
using Wasnie.Application.Compensation.Commands.Credits;
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
    IBackgroundJobService backgroundJobService)
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
        // Join via transaction date since Credit has no direct date field.
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
        int supersededCount = 0;
        var affectedPayeeIds = new HashSet<Guid>();

        // Supersede credits for processable transactions only
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

            if (tx.PayeeId.HasValue)
                affectedPayeeIds.Add(tx.PayeeId.Value);
        }

        await db.SaveChangesAsync(cancellationToken);

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
            new RecalculateCreditsResult(supersededCount, skippedPaidCount, jobIds));
    }
}
