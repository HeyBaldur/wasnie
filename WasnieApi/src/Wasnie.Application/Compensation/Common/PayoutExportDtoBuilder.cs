using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Compensation.DTOs;
using Wasnie.Application.Compensation.Handlers.Payouts;

namespace Wasnie.Application.Compensation.Common;

/// <summary>
/// Loads one payout as the <see cref="PayoutDto"/> that an export renders.
///
/// ★★ IT IS SHARED SO THE TWO EXPORTS CANNOT DISAGREE. The PDF and the Excel are two renderings of the
/// SAME payout, and a payee who receives both must not be able to find a different total in each. Two
/// copies of this loading code would eventually drift — one gains a field, one resolves an actor
/// differently — and the day they disagree, neither can be defended.
///
/// ★ IT LOADS, IT DOES NOT DECIDE. No filtering, no rounding, no formatting: the lines come from
/// <c>GetPayoutByIdHandler.BuildLinesAsync</c>, the same builder the screen uses, so the file agrees
/// with what the reader saw before pressing the button.
/// </summary>
public static class PayoutExportDtoBuilder
{
    public static async Task<PayoutDto?> LoadAsync(
        IApplicationDbContext db,
        IIdentityService identityService,
        Guid payoutId,
        CancellationToken cancellationToken)
    {
        var payout = await db.CompensationPayouts
            .Include(p => p.Lines)
            .FirstOrDefaultAsync(p => p.Id == payoutId, cancellationToken);

        if (payout is null) return null;

        var calculatedByDisplay = await ResolveActorDisplayAsync(identityService, payout.CalculatedBy);
        var updatedByDisplay = await ResolveActorDisplayAsync(identityService, payout.UpdatedBy);

        var planName = await db.CompensationPlans
            .Where(p => p.Id == payout.PlanId)
            .Select(p => p.Name)
            .FirstOrDefaultAsync(cancellationToken) ?? string.Empty;

        var lines = await GetPayoutByIdHandler.BuildLinesAsync(
            payout.Lines, db, payout.Id, cancellationToken);

        return new PayoutDto(
            Id: payout.Id,
            TenantId: payout.TenantId,
            PayeeId: payout.PayeeId,
            PayeeName: payout.PayeeSnapshot.FullName,
            PayeeCode: payout.PayeeSnapshot.EmployeeCode,
            PlanId: payout.PlanId,
            PlanName: planName,
            PeriodStart: payout.Period.Start,
            PeriodEnd: payout.Period.End,
            TotalCommissionAmount: payout.TotalCommission.Amount,
            TotalCommissionCurrency: payout.TotalCommission.Currency,
            Status: payout.Status.ToString(),
            CalculatedAt: payout.CalculatedAt,
            CalculatedBy: calculatedByDisplay,
            UpdatedAt: payout.UpdatedAt,
            UpdatedBy: updatedByDisplay,
            Lines: lines);
    }

    /// <summary>
    /// If the stored actor is a GUID (legacy data before the email fix), resolve it to an email.
    /// Returns the original value unchanged for emails and "system".
    /// </summary>
    private static async Task<string> ResolveActorDisplayAsync(
        IIdentityService identityService, string actor)
    {
        if (Guid.TryParse(actor, out _))
            return await identityService.FindEmailByUserIdAsync(actor) ?? actor;
        return actor;
    }
}
