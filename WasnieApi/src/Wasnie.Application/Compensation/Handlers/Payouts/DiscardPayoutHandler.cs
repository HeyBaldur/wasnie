using MediatR;
using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Common.Abstractions;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Compensation.Commands.Payouts;
using Wasnie.Domain.Authorization;
using Wasnie.Domain.Common.Results;
using Wasnie.Domain.Exceptions;

namespace Wasnie.Application.Compensation.Handlers.Payouts;

/// <summary>
/// Gives an unpayable Approved payout a way out.
///
/// ★★ IT MOVES NO MONEY AND TOUCHES NO CREDIT. The credits stay consumed by the payout that really
/// paid them, the transactions stay Paid, and nothing is reversed: this records that a figure which
/// could never be paid has stopped being owed. Reversing a payment is
/// <c>RevertPaidToApproved</c> — a different operation, on a different status, with different
/// consequences.
/// </summary>
public sealed class DiscardPayoutHandler(
    IApplicationDbContext db,
    ICurrentUserService currentUser,
    IClock clock,
    IAuthorizationService authorizationService)
    : IRequestHandler<DiscardPayoutCommand, Result<DiscardPayoutResult>>
{
    public async Task<Result<DiscardPayoutResult>> Handle(
        DiscardPayoutCommand request, CancellationToken ct)
    {
        // ★ Payouts.Discard, not Payouts.MarkPaid. Paying and deciding something will never be paid
        // are different rights — see Permission.PayoutsDiscard.
        await authorizationService.RequireAsync(Permission.PayoutsDiscard, ct);

        if (string.IsNullOrWhiteSpace(request.Reason))
            return Result<DiscardPayoutResult>.Failure("A discarded payout must state why it is being closed.");

        var payout = await db.CompensationPayouts
            .Include(p => p.Lines)
            .FirstOrDefaultAsync(p => p.Id == request.PayoutId, ct);

        if (payout is null)
            return Result<DiscardPayoutResult>.Failure("Payout not found.");

        // ══ THE MONEY GUARD ══════════════════════════════════════════════════════════════════════
        //
        // ★★ EVERY LIVE CREDIT MUST ALREADY BE PAID BY ANOTHER PAYOUT — not merely one of them, and
        // not merely "the payment was blocked". A payout can be blocked by a single duplicated credit
        // while still carrying dozens that nobody has paid: measured in this database, one such payout
        // held 71 credits already paid and 139 that were not, worth the larger part of €34,567.64.
        // Discarding that would retire a real debt to a real person, silently, on the strength of a
        // button whose label says the money was already paid.
        //
        // ★ SUPERSEDED CREDITS ARE NOT COUNTED. A superseded credit was replaced by a later
        // calculation and is worth zero; requiring it to be "already paid" would refuse to discard
        // payouts that are genuinely unpayable.
        var creditIds = payout.Lines.Select(l => l.CreditId).ToList();

        var liveCredits = await db.Credits
            .Where(c => creditIds.Contains(c.Id) && c.SupersededAt == null)
            .Select(c => new { c.Id, c.ConsumedAt, c.ConsumedByPayoutId })
            .ToListAsync(ct);

        var paidElsewhere = liveCredits
            .Count(c => c.ConsumedAt != null && c.ConsumedByPayoutId != payout.Id);

        var unpaid = liveCredits.Count - paidElsewhere;

        if (liveCredits.Count == 0)
            return Result<DiscardPayoutResult>.Failure(
                "This payout carries no live credits, so there is nothing a discard would resolve.");

        if (unpaid > 0)
        {
            // Reported with the count rather than a bare refusal: the reader needs to know this is
            // not a permission problem but money they still owe.
            return Result<DiscardPayoutResult>.Failure(
                $"This payout still has {unpaid} credit(s) that no other payout has paid. "
                + "Discarding it would retire commission that is still owed; recalculate the period instead.");
        }

        try
        {
            payout.Discard(request.Reason, currentUser.UserId ?? "system", clock.UtcNowOffset);
        }
        catch (DomainException ex)
        {
            return Result<DiscardPayoutResult>.Failure(ex.Message);
        }

        await db.SaveChangesAsync(ct);

        return Result<DiscardPayoutResult>.Success(new DiscardPayoutResult(
            PayoutId: payout.Id,
            PayeeName: payout.PayeeSnapshot.FullName,
            Amount: payout.TotalCommission.Amount,
            Currency: payout.TotalCommission.Currency,
            CreditsAlreadyPaidElsewhere: paidElsewhere));
    }
}
