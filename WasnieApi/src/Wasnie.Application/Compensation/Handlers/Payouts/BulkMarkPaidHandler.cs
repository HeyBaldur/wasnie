using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Wasnie.Application.Common.Abstractions;
using Wasnie.Application.Common.DTOs;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Compensation.Queries.Payouts;
using Wasnie.Domain.Audit;
using Wasnie.Domain.Authorization;
using Wasnie.Domain.Common.Results;
using Wasnie.Domain.Compensation.Enums;
using Wasnie.Domain.Exceptions;

namespace Wasnie.Application.Compensation.Handlers.Payouts;

public sealed class BulkMarkPaidHandler(
    IApplicationDbContext db,
    IAuthorizationService authorizationService,
    ICurrentUserService currentUser,
    IClock clock,
    IAuditService audit,
    ILogger<BulkMarkPaidHandler> logger)
    : IRequestHandler<BulkMarkPaidCommand, Result<BulkMarkPaidResult>>
{
    public async Task<Result<BulkMarkPaidResult>> Handle(
        BulkMarkPaidCommand request, CancellationToken cancellationToken)
    {
        await authorizationService.RequireAsync(Permission.PayoutsMarkPaid, cancellationToken);

        if (request.PayoutIds.Count == 0)
            return Result<BulkMarkPaidResult>.Success(new BulkMarkPaidResult(0, []));

        var payouts = await db.CompensationPayouts
            .Include(p => p.Lines)
            .Where(p => request.PayoutIds.Contains(p.Id))
            .ToListAsync(cancellationToken);

        var now   = clock.UtcNowOffset;
        var actor = currentUser.Email ?? currentUser.UserId ?? "system";
        var paid   = 0;
        var errors = new List<string>();

        var overlapCount = 0;
        foreach (var p in payouts)
        {
            var hasOverlap = await db.CompensationPayouts
                .AnyAsync(other =>
                    other.PayeeId == p.PayeeId &&
                    other.Id != p.Id &&
                    (other.Status == CompensationPayoutStatus.Approved ||
                     other.Status == CompensationPayoutStatus.Paid) &&
                    other.Period.Start <= p.Period.End &&
                    other.Period.End >= p.Period.Start,
                    cancellationToken);
            if (hasOverlap) overlapCount++;
        }

        // Batch-load all credits for all payouts in this bulk operation.
        var allCreditIds = payouts.SelectMany(p => p.Lines.Select(l => l.CreditId)).Distinct().ToList();
        var allTenantId  = payouts.Count > 0 ? payouts[0].TenantId : Guid.Empty;

        Dictionary<Guid, Domain.Compensation.Credits.Credit> allCreditById = [];
        Dictionary<Guid, Domain.Compensation.Transactions.CompensationTransaction> txById = [];

        if (allCreditIds.Count > 0)
        {
            var allCredits = await db.Credits
                .IgnoreQueryFilters()
                .Where(c => allCreditIds.Contains(c.Id) && c.TenantId == allTenantId)
                .ToListAsync(cancellationToken);

            allCreditById = allCredits.ToDictionary(c => c.Id);

            var allTxIds = allCredits.Where(c => c.SupersededAt == null).Select(c => c.TransactionId).Distinct().ToList();
            if (allTxIds.Count > 0)
            {
                var allTxs = await db.CompensationTransactions
                    .IgnoreQueryFilters()
                    .Where(t => allTxIds.Contains(t.Id)
                             && t.TenantId == allTenantId
                             && t.Status == CompensationTransactionStatus.Calculated)
                    .ToListAsync(cancellationToken);

                txById = allTxs.ToDictionary(t => t.Id);
            }
        }

        foreach (var payout in payouts)
        {
            try
            {
                // ── ANTI-DOUBLE-PAY GUARD (per payout) ───────────────────────────
                var payoutCreditIds = payout.Lines.Select(l => l.CreditId).ToHashSet();
                var payoutCredits = payoutCreditIds
                    .Where(id => allCreditById.ContainsKey(id))
                    .Select(id => allCreditById[id])
                    .ToList();

                var alreadyConsumed = payoutCredits.Where(c => c.ConsumedAt is not null).ToList();
                if (alreadyConsumed.Count > 0)
                {
                    var conflictDesc = alreadyConsumed
                        .Select(c => $"credit {c.Id} (tx {c.TransactionId}) consumed by {c.ConsumedByPayoutId}")
                        .Take(3);
                    var msg = $"Payout {payout.Id} ({payout.PayeeSnapshot.FullName}): {alreadyConsumed.Count} transaction(s) already paid — "
                        + string.Join(", ", conflictDesc);
                    errors.Add(msg);
                    logger.LogWarning(
                        "Bulk: Payout {PayoutId} payment BLOCKED — {Count} credits already consumed.",
                        payout.Id, alreadyConsumed.Count);
                    continue;
                }

                payout.MarkPaid(actor, now);

                var consumedTxIds = new HashSet<Guid>();
                foreach (var credit in payoutCredits)
                {
                    if (credit.SupersededAt is not null)
                        continue;
                    credit.Consume(payout.Id, now, Guid.NewGuid());
                    consumedTxIds.Add(credit.TransactionId);
                }

                foreach (var txId in consumedTxIds)
                {
                    if (txById.TryGetValue(txId, out var tx))
                        tx.MarkPaid(actor, now, Guid.NewGuid());
                }

                paid++;
            }
            catch (DomainException ex)
            {
                errors.Add($"Payout {payout.Id} ({payout.PayeeSnapshot.FullName}): {ex.Message}");
            }
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            logger.LogError(ex, "Bulk mark-paid blocked by concurrent update on a shared credit.");
            return Result<BulkMarkPaidResult>.Failure(
                "One or more payments could not be processed: a concurrent payment consumed a shared credit. Please refresh and try again.");
        }

        if (overlapCount > 0 && payouts.Count > 0)
        {
            await audit.LogAsync(new AuditEntry(
                TenantId: payouts[0].TenantId,
                Action: AuditActions.PayoutBulkPaidWithOverlap,
                ResourceType: "CompensationPayout",
                ResourceId: string.Join(",", request.PayoutIds),
                ActorUserId: currentUser.UserId ?? actor,
                ActorEmail: actor,
                Metadata: new Dictionary<string, string>
                {
                    ["payoutsWithOverlapCount"] = overlapCount.ToString(),
                    ["totalPayoutsInBatch"]     = payouts.Count.ToString(),
                }), cancellationToken);
        }

        return Result<BulkMarkPaidResult>.Success(new BulkMarkPaidResult(paid, errors));
    }
}
