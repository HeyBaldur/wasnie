using System.Globalization;
using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Common.Abstractions;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Compensation.Commands.Transactions;
using Wasnie.Application.Compensation.DTOs;
using Wasnie.Domain.Audit;
using Wasnie.Domain.Authorization;
using Wasnie.Domain.Common.Results;
using Wasnie.Domain.Compensation.Enums;
using Wasnie.Domain.Exceptions;

namespace Wasnie.Application.Compensation.Handlers.Transactions;

/// <summary>
/// Reverts a Calculated commission whose CRM deal was lost. Money-critical, non-destructive: supersedes the
/// live credits (never deletes) and cancels the transaction, so the credit drops out of any future pay run
/// (CalculatePayouts filters SupersededAt == null). Hard guards: only a transaction with an unresolved
/// deal-lost alert, only Calculated (Paid → honest refusal), and never one whose credit is already committed
/// to an Approved/Paid payout.
/// </summary>
public sealed class RevertCommissionForLostDealHandler(
    IApplicationDbContext db,
    ICurrentUserService currentUser,
    IClock clock,
    IGuidGenerator guid,
    IAuthorizationService authorizationService)
    : IRequestHandler<RevertCommissionForLostDealCommand, Result<TransactionDto>>
{
    public async Task<Result<TransactionDto>> Handle(
        RevertCommissionForLostDealCommand request, CancellationToken cancellationToken)
    {
        await authorizationService.RequireAsync(Permission.TransactionsVoid, cancellationToken);

        var transaction = await db.CompensationTransactions
            .FirstOrDefaultAsync(t => t.Id == request.TransactionId, cancellationToken);
        if (transaction is null)
            return Result<TransactionDto>.Failure("Transaction not found.");

        // Only actionable when the reverse reconciliation actually flagged this deal as lost — the revert is
        // a response to that alert, not a generic void of any Calculated commission.
        var alert = await db.DealLostAlerts
            .FirstOrDefaultAsync(a => a.TransactionId == request.TransactionId && a.ResolvedAt == null, cancellationToken);
        if (alert is null)
            return Result<TransactionDto>.Failure("There is no open lost-deal alert for this transaction.");

        // Honest, explicit block for Paid (clawback out of scope). The domain method also guards, but this
        // gives the precise message before any state is touched.
        if (transaction.Status == CompensationTransactionStatus.Paid)
            return Result<TransactionDto>.Failure(
                "This commission was already paid; reverting a paid commission (clawback) is not supported here.");

        if (transaction.Status != CompensationTransactionStatus.Calculated)
            return Result<TransactionDto>.Failure(
                $"Only a Calculated commission can be reverted for a lost deal. Current status: {transaction.Status}.");

        // The transaction's live (non-superseded) credits — what would be reverted.
        var liveCredits = await db.Credits
            .Where(c => c.TransactionId == request.TransactionId && c.SupersededAt == null)
            .ToListAsync(cancellationToken);

        // ── Danger-zone guard ────────────────────────────────────────────────────────────────────
        // A Calculated transaction's credit can already be committed to an Approved (not yet Paid) payout —
        // Approve does not change the transaction status. Superseding it would desync that payout. Block and
        // tell the admin to revert the payout first. (ConsumedAt covers Paid; Approved needs this explicit check.)
        var liveCreditIds = liveCredits.Select(c => c.Id).ToList();
        if (liveCreditIds.Count > 0)
        {
            var committed = await db.CompensationPayouts
                .Where(p => (p.Status == CompensationPayoutStatus.Approved
                          || p.Status == CompensationPayoutStatus.Paid)
                         && p.Lines.Any(l => liveCreditIds.Contains(l.CreditId)))
                .AnyAsync(cancellationToken);
            if (committed)
                return Result<TransactionDto>.Failure(
                    "This commission is part of an approved or paid payout. Revert that payout to Approved/Draft first, then retry.");
        }

        var now = clock.UtcNowOffset;
        var actor = currentUser.UserId ?? "system";
        var reason = $"Deal lost in CRM (deal {alert.ExternalDealId}).";

        try
        {
            foreach (var credit in liveCredits)
                credit.Supersede(reason, now, guid.NewGuid());

            transaction.RevertForLostDeal(reason, actor, now, guid.NewGuid());
        }
        catch (DomainException ex)
        {
            return Result<TransactionDto>.Failure(ex.Message);
        }

        alert.Resolve(actor, now);

        // Money trail: explicit audit with the reverted amount alongside the command-level audit.
        db.AuditLogs.Add(AuditLog.Create(
            tenantId: transaction.TenantId,
            timestampUtc: now.UtcDateTime,
            actorUserId: actor,
            actorEmail: currentUser.Email ?? actor,
            action: AuditActions.DealLostCommissionReverted,
            resourceType: ResourceTypes.Transaction,
            resourceId: transaction.Id.ToString(),
            resourceDisplayName: transaction.ReferenceNumber,
            beforeJson: JsonSerializer.Serialize(new
            {
                status = nameof(CompensationTransactionStatus.Calculated),
                externalDealId = alert.ExternalDealId,
                revertedAmount = alert.CommissionAmount.ToString(CultureInfo.InvariantCulture),
                revertedCurrency = alert.CommissionCurrency,
                supersededCredits = liveCredits.Count,
            }),
            afterJson: JsonSerializer.Serialize(new
            {
                status = nameof(CompensationTransactionStatus.Cancelled),
                reason,
            })));

        await db.SaveChangesAsync(cancellationToken);
        request.AuditResourceId = transaction.Id.ToString();

        return Result<TransactionDto>.Success(IngestTransactionHandler.ToDto(transaction));
    }
}
