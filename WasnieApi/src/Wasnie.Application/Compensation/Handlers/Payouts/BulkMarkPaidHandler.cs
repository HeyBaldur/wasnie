using MediatR;
using Microsoft.EntityFrameworkCore;
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
    IAuditService audit)
    : IRequestHandler<BulkMarkPaidCommand, Result<BulkMarkPaidResult>>
{
    public async Task<Result<BulkMarkPaidResult>> Handle(
        BulkMarkPaidCommand request, CancellationToken cancellationToken)
    {
        await authorizationService.RequireAsync(Permission.PayoutsMarkPaid, cancellationToken);

        if (request.PayoutIds.Count == 0)
            return Result<BulkMarkPaidResult>.Success(new BulkMarkPaidResult(0, []));

        var payouts = await db.CompensationPayouts
            .Where(p => request.PayoutIds.Contains(p.Id))
            .ToListAsync(cancellationToken);

        var now   = clock.UtcNowOffset;
        var actor = currentUser.Email ?? currentUser.UserId ?? "system";
        var paid   = 0;
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
                payout.MarkPaid(actor, now);
                paid++;
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
