using MediatR;
using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Common.Abstractions;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Compensation.Queries.Payouts;
using Wasnie.Domain.Authorization;
using Wasnie.Domain.Common.Results;
using Wasnie.Domain.Exceptions;

namespace Wasnie.Application.Compensation.Handlers.Payouts;

public sealed class BulkMarkPaidHandler(
    IApplicationDbContext db,
    IAuthorizationService authorizationService,
    ICurrentUserService currentUser,
    IClock clock)
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

        var now = clock.UtcNowOffset;
        var actor = currentUser.Email ?? currentUser.UserId ?? "system";
        var paid = 0;
        var errors = new List<string>();

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

        return Result<BulkMarkPaidResult>.Success(new BulkMarkPaidResult(paid, errors));
    }
}
