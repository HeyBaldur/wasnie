using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Wasnie.Application.Common.Abstractions;
using Wasnie.Application.Common.DTOs;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Compensation.Commands.Payouts;
using Wasnie.Domain.Audit;
using Wasnie.Domain.Authorization;
using Wasnie.Domain.Common.Results;
using Wasnie.Domain.Compensation.Enums;

namespace Wasnie.Application.Compensation.Handlers.Payouts;

public sealed class MarkPayoutPaidHandler(
    IApplicationDbContext db,
    IAuthorizationService authorizationService,
    ICurrentUserService currentUser,
    IClock clock,
    IAuditService audit,
    ILogger<MarkPayoutPaidHandler> logger)
    : IRequestHandler<MarkPayoutPaidCommand, Result<PaymentBlockResult?>>
{
    public async Task<Result<PaymentBlockResult?>> Handle(MarkPayoutPaidCommand request, CancellationToken cancellationToken)
    {
        await authorizationService.RequireAsync(Permission.PayoutsMarkPaid, cancellationToken);

        var payout = await db.CompensationPayouts
            .Include(p => p.Lines)
            .FirstOrDefaultAsync(p => p.Id == request.PayoutId, cancellationToken);

        if (payout is null)
            return Result<PaymentBlockResult?>.Failure("Payout not found.");

        try
        {
            var actor = currentUser.Email ?? currentUser.UserId ?? "system";
            var now = clock.UtcNowOffset;
            var tenantId = payout.TenantId;

            // Capture overlapping Approved/Paid payouts before transition (for audit).
            var payeeId     = payout.PayeeId;
            var periodStart = payout.Period.Start;
            var periodEnd   = payout.Period.End;

            var overlappingIds = await db.CompensationPayouts
                .Where(p => p.Id != request.PayoutId
                         && p.PayeeId == payeeId
                         && (p.Status == CompensationPayoutStatus.Approved || p.Status == CompensationPayoutStatus.Paid)
                         && p.Period.Start <= periodEnd
                         && p.Period.End >= periodStart)
                .Select(p => p.Id)
                .ToListAsync(cancellationToken);

            var creditIds = payout.Lines.Select(l => l.CreditId).ToList();

            List<Domain.Compensation.Credits.Credit> credits = [];
            Dictionary<Guid, Domain.Compensation.Transactions.CompensationTransaction> txById = [];

            if (creditIds.Count > 0)
            {
                credits = await db.Credits
                    .IgnoreQueryFilters()
                    .Where(c => creditIds.Contains(c.Id) && c.TenantId == tenantId)
                    .ToListAsync(cancellationToken);

                // ── ANTI-DOUBLE-PAY GUARD ─────────────────────────────────────────
                var alreadyConsumed = credits.Where(c => c.ConsumedAt is not null).ToList();
                if (alreadyConsumed.Count > 0)
                {
                    var consumedTxIds = alreadyConsumed.Select(c => c.TransactionId).Distinct().ToList();
                    var consumingPayoutIds = alreadyConsumed
                        .Where(c => c.ConsumedByPayoutId.HasValue)
                        .Select(c => c.ConsumedByPayoutId!.Value).Distinct().ToList();

                    var txRefs = await db.CompensationTransactions
                        .IgnoreQueryFilters()
                        .Where(t => consumedTxIds.Contains(t.Id) && t.TenantId == tenantId)
                        .Select(t => new { t.Id, t.ReferenceNumber })
                        .ToListAsync(cancellationToken);

                    var consumingPayouts = await db.CompensationPayouts
                        .IgnoreQueryFilters()
                        .Where(p => consumingPayoutIds.Contains(p.Id) && p.TenantId == tenantId)
                        .ToListAsync(cancellationToken);

                    var txRefById = txRefs.ToDictionary(t => t.Id, t => t.ReferenceNumber);
                    var consumingPayoutPeriod = consumingPayouts
                        .ToDictionary(p => p.Id, p => (Start: p.Period.Start.ToString("yyyy-MM-dd"), End: p.Period.End.ToString("yyyy-MM-dd")));

                    var conflictItems = alreadyConsumed
                        .Select(c =>
                        {
                            var txRef = txRefById.TryGetValue(c.TransactionId, out var r) ? r : c.TransactionId.ToString();
                            var payoutId = c.ConsumedByPayoutId ?? Guid.Empty;
                            consumingPayoutPeriod.TryGetValue(payoutId, out var period);
                            return new PaymentConflictItem(txRef, payoutId, period.Start ?? "?", period.End ?? "?");
                        })
                        .DistinctBy(x => x.TransactionReference)
                        .ToList();

                    var conflictSummary = string.Join("; ", conflictItems.Take(5).Select(c =>
                        $"{c.TransactionReference} → payout {c.PaidInPayoutId} ({c.PaidInPayoutPeriodStart} – {c.PaidInPayoutPeriodEnd})"));

                    logger.LogWarning(
                        "Payout {PayoutId} payment BLOCKED — {Count} credits already consumed. Conflicts: {Conflicts}",
                        request.PayoutId, alreadyConsumed.Count, conflictSummary);

                    await audit.LogAsync(new AuditEntry(
                        TenantId: tenantId,
                        Action: AuditActions.PaymentBlockedDoublePayment,
                        ResourceType: "CompensationPayout",
                        ResourceId: request.PayoutId.ToString(),
                        ActorUserId: currentUser.UserId ?? actor,
                        ActorEmail: actor,
                        Metadata: new Dictionary<string, string>
                        {
                            ["blockedCreditCount"] = alreadyConsumed.Count.ToString(),
                            ["conflicts"]          = conflictSummary,
                        }), cancellationToken);

                    return Result<PaymentBlockResult?>.Success(new PaymentBlockResult(alreadyConsumed.Count, conflictItems));
                }

                var txIds = credits.Where(c => c.SupersededAt == null).Select(c => c.TransactionId).Distinct().ToList();
                if (txIds.Count > 0)
                {
                    var txs = await db.CompensationTransactions
                        .IgnoreQueryFilters()
                        .Where(t => txIds.Contains(t.Id)
                                 && t.TenantId == tenantId
                                 && t.Status == CompensationTransactionStatus.Calculated)
                        .ToListAsync(cancellationToken);

                    txById = txs.ToDictionary(t => t.Id);
                }
            }

            // ── Phase 3: mark payout Paid (Approved → Paid) ─────────────────────
            payout.MarkPaid(actor, now);

            var consumedCreditCount = 0;
            var paidTxCount = 0;

            var consumedTxIdsForPayout = new HashSet<Guid>();
            foreach (var credit in credits)
            {
                if (credit.SupersededAt is not null)
                    continue;

                credit.Consume(payout.Id, now, Guid.NewGuid());
                consumedCreditCount++;
                consumedTxIdsForPayout.Add(credit.TransactionId);
            }

            foreach (var txId in consumedTxIdsForPayout)
            {
                if (txById.TryGetValue(txId, out var tx))
                {
                    tx.MarkPaid(actor, now, Guid.NewGuid());
                    paidTxCount++;
                }
            }

            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException ex)
            {
                logger.LogError(ex,
                    "Payout {PayoutId} payment blocked by concurrent update — another payment consumed a shared credit.",
                    request.PayoutId);
                return Result<PaymentBlockResult?>.Failure(
                    "Payment could not be processed: a concurrent payment already consumed one or more credits for this payout. Please refresh and try again.");
            }

            // ── Audit: overlap warning ────────────────────────────────────────────
            if (overlappingIds.Count > 0)
            {
                await audit.LogAsync(new AuditEntry(
                    TenantId: tenantId,
                    Action: AuditActions.PayoutPaidWithOverlap,
                    ResourceType: "CompensationPayout",
                    ResourceId: request.PayoutId.ToString(),
                    ActorUserId: currentUser.UserId ?? actor,
                    ActorEmail: actor,
                    Metadata: new Dictionary<string, string>
                    {
                        ["overlappingPayoutIds"] = string.Join(",", overlappingIds),
                        ["overlapCount"]         = overlappingIds.Count.ToString(),
                    }), cancellationToken);
            }

            await audit.LogAsync(new AuditEntry(
                TenantId: tenantId,
                Action: AuditActions.PayoutCreditsConsumed,
                ResourceType: "CompensationPayout",
                ResourceId: request.PayoutId.ToString(),
                ActorUserId: currentUser.UserId ?? actor,
                ActorEmail: actor,
                Metadata: new Dictionary<string, string>
                {
                    ["creditsConsumed"]  = consumedCreditCount.ToString(),
                    ["transactionsPaid"] = paidTxCount.ToString(),
                }), cancellationToken);

            return Result<PaymentBlockResult?>.Success(null);
        }
        catch (Domain.Exceptions.DomainException ex)
        {
            return Result<PaymentBlockResult?>.Failure(ex.Message);
        }
    }
}
