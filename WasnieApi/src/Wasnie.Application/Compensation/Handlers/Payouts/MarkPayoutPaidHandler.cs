using MediatR;
using Microsoft.EntityFrameworkCore;
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
    IAuditService audit)
    : IRequestHandler<MarkPayoutPaidCommand, Result>
{
    public async Task<Result> Handle(MarkPayoutPaidCommand request, CancellationToken cancellationToken)
    {
        await authorizationService.RequireAsync(Permission.PayoutsMarkPaid, cancellationToken);

        var payout = await db.CompensationPayouts
            .FirstOrDefaultAsync(p => p.Id == request.PayoutId, cancellationToken);

        if (payout is null)
            return Result.Failure("Payout not found.");

        try
        {
            var actor = currentUser.Email ?? currentUser.UserId ?? "system";
            var now = clock.UtcNowOffset;

            // Capture overlapping Approved/Paid payouts for the same payee before transition (for audit).
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

            payout.MarkPaid(actor, now);

            await db.SaveChangesAsync(cancellationToken);

            if (overlappingIds.Count > 0)
            {
                await audit.LogAsync(new AuditEntry(
                    TenantId: payout.TenantId,
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

            return Result.Success();
        }
        catch (Domain.Exceptions.DomainException ex)
        {
            return Result.Failure(ex.Message);
        }
    }
}
