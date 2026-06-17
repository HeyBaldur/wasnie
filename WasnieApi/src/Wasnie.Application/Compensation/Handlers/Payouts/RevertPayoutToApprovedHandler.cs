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

public sealed class RevertPayoutToApprovedHandler(
    IApplicationDbContext db,
    IAuthorizationService authorizationService,
    ICurrentUserService currentUser,
    IClock clock,
    IAuditService audit,
    ILogger<RevertPayoutToApprovedHandler> logger)
    : IRequestHandler<RevertPayoutToApprovedCommand, Result>
{
    public async Task<Result> Handle(RevertPayoutToApprovedCommand request, CancellationToken cancellationToken)
    {
        await authorizationService.RequireAsync(Permission.PayoutsReopen, cancellationToken);

        var payout = await db.CompensationPayouts
            .FirstOrDefaultAsync(p => p.Id == request.PayoutId, cancellationToken);

        if (payout is null)
            return Result.Failure("Payout not found.");

        try
        {
            var actor = currentUser.Email ?? currentUser.UserId ?? "system";
            var now = clock.UtcNowOffset;

            // Load all credits consumed by this payout.
            var consumedCredits = await db.Credits
                .IgnoreQueryFilters()
                .Where(c => c.ConsumedByPayoutId == request.PayoutId
                         && c.TenantId == payout.TenantId)
                .ToListAsync(cancellationToken);

            // Load the Paid transactions associated with those credits.
            var txIds = consumedCredits.Select(c => c.TransactionId).Distinct().ToList();
            var paidTransactions = txIds.Count > 0
                ? await db.CompensationTransactions
                    .IgnoreQueryFilters()
                    .Where(t => txIds.Contains(t.Id)
                             && t.TenantId == payout.TenantId
                             && t.Status == CompensationTransactionStatus.Paid)
                    .ToListAsync(cancellationToken)
                : [];

            // Revert payout status: Paid → Approved.
            payout.RevertPaidToApproved(actor, now);

            // Unconsume each credit, returning it to the available pool.
            foreach (var credit in consumedCredits)
                credit.Unconsume(Guid.NewGuid(), now);

            // Revert transaction status: Paid → Calculated.
            foreach (var tx in paidTransactions)
                tx.RevertPaidToCalculated(actor, now);

            logger.LogInformation(
                "RevertPayoutToApproved: payout={PayoutId} reverted; unconsumed {CreditCount} credits, " +
                "reverted {TxCount} transactions to Calculated.",
                request.PayoutId, consumedCredits.Count, paidTransactions.Count);

            await db.SaveChangesAsync(cancellationToken);

            await audit.LogAsync(new AuditEntry(
                TenantId: payout.TenantId,
                Action: AuditActions.PayoutRevertedToApproved,
                ResourceType: "CompensationPayout",
                ResourceId: request.PayoutId.ToString(),
                ActorUserId: currentUser.UserId ?? actor,
                ActorEmail: actor,
                Metadata: new Dictionary<string, string>
                {
                    ["creditsUnconsumed"]         = consumedCredits.Count.ToString(),
                    ["transactionsReverted"]       = paidTransactions.Count.ToString(),
                }), cancellationToken);

            return Result.Success();
        }
        catch (Domain.Exceptions.DomainException ex)
        {
            return Result.Failure(ex.Message);
        }
    }
}
