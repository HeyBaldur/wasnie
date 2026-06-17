using MediatR;
using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Common.DTOs;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Compensation.Queries.Payouts;
using Wasnie.Domain.Audit;
using Wasnie.Domain.Authorization;
using Wasnie.Domain.Common.Results;
using Wasnie.Domain.Compensation.Enums;
using Wasnie.Domain.Exceptions;

namespace Wasnie.Application.Compensation.Handlers.Payouts;

public sealed class BulkApprovePayoutsHandler(
    IApplicationDbContext db,
    IAuthorizationService authorizationService,
    ICurrentUserService currentUser,
    IAuditService audit)
    : IRequestHandler<BulkApprovePayoutsCommand, Result<BulkApproveResult>>
{
    public async Task<Result<BulkApproveResult>> Handle(
        BulkApprovePayoutsCommand request, CancellationToken cancellationToken)
    {
        await authorizationService.RequireAsync(Permission.PayoutsApprove, cancellationToken);

        if (request.PayoutIds.Count == 0)
            return Result<BulkApproveResult>.Success(new BulkApproveResult(0, []));

        var payouts = await db.CompensationPayouts
            .Where(p => request.PayoutIds.Contains(p.Id))
            .ToListAsync(cancellationToken);

        var now    = DateTimeOffset.UtcNow;
        var actor  = currentUser.Email ?? currentUser.UserId ?? "system";
        var approved = 0;
        var errors = new List<string>();

        // Count how many payouts in this batch have at least one overlapping Approved/Paid payout.
        var overlapCount = 0;
        foreach (var p in payouts)
        {
            var id          = p.Id;
            var payeeId     = p.PayeeId;
            var periodStart = p.Period.Start;
            var periodEnd   = p.Period.End;

            var hasOverlap = await db.CompensationPayouts
                .AnyAsync(other =>
                    other.PayeeId == payeeId &&
                    other.Id != id &&
                    (other.Status == CompensationPayoutStatus.Approved ||
                     other.Status == CompensationPayoutStatus.Paid) &&
                    other.Period.Start <= periodEnd &&
                    other.Period.End >= periodStart,
                    cancellationToken);

            if (hasOverlap) overlapCount++;
        }

        foreach (var payout in payouts)
        {
            try
            {
                payout.Approve(actor, now, Guid.NewGuid());
                approved++;
            }
            catch (DomainException ex)
            {
                errors.Add($"Payout {payout.Id} ({payout.PayeeSnapshot.FullName}): {ex.Message}");
            }
        }

        await db.SaveChangesAsync(cancellationToken);

        if (overlapCount > 0 && payouts.Count > 0)
        {
            await audit.LogAsync(new AuditEntry(
                TenantId: payouts[0].TenantId,
                Action: AuditActions.PayoutBulkApprovedWithOverlap,
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

        return Result<BulkApproveResult>.Success(new BulkApproveResult(approved, errors));
    }
}
