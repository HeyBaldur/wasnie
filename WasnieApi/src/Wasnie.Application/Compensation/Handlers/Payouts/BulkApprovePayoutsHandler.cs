using MediatR;
using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Compensation.Queries.Payouts;
using Wasnie.Domain.Authorization;
using Wasnie.Domain.Common.Results;
using Wasnie.Domain.Exceptions;

namespace Wasnie.Application.Compensation.Handlers.Payouts;

public sealed class BulkApprovePayoutsHandler(
    IApplicationDbContext db,
    IAuthorizationService authorizationService,
    ICurrentUserService currentUser)
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

        var now = DateTimeOffset.UtcNow;
        var actor = currentUser.Email ?? currentUser.UserId ?? "system";
        var approved = 0;
        var errors = new List<string>();

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

        return Result<BulkApproveResult>.Success(new BulkApproveResult(approved, errors));
    }
}
