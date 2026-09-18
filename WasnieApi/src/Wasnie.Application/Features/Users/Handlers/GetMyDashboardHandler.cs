using MediatR;
using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Common.Abstractions;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Compensation.Calculation;
using Wasnie.Application.Compensation.Common;
using Wasnie.Application.Compensation.Queries.Ledger;
using Wasnie.Application.Features.Users.Queries;
using Wasnie.Domain.Authorization;
using Wasnie.Domain.Common.Results;
using Wasnie.Domain.Compensation.Quotas;
using Wasnie.Domain.Compensation.Enums;

namespace Wasnie.Application.Features.Users.Handlers;

/// <summary>
/// The signed-in person's own commissions, balance and attainment (KAN-92, batch 3).
///
/// ★★ THIS EXISTS BECAUSE THE COMPANY DASHBOARD IS NOT EVERYONE'S DASHBOARD. A rep landing on the
/// admin overview saw every figure at zero and could only conclude one of two wrong things: that the
/// product was broken, or that they had earned nothing.
///
/// ★★ THE PAYEE COMES FROM THE TOKEN AND NOWHERE ELSE. There is no payee id in the request, because a
/// request that could name one would let anybody read anybody's pay. It is resolved through
/// <c>Payee.UserId</c>, which batch 2 made an administrator set on purpose rather than a match on
/// email — the whole reason that link is explicit is so this line can be trusted.
///
/// ★★ NOT LINKED IS AN ANSWER, NOT AN EMPTY RESULT. A person whose account was never attached to a
/// payee gets <c>Linked = false</c> and a screen that says so. Returning zeros would be the false zero
/// this codebase already has a name for, and it would arrive dressed as a working page: somebody who
/// HAS earned money would read that they had not.
///
/// ★★ EVERY FIGURE COMES FROM THE SURFACE THAT ALREADY OWNS IT. The money is the existing ledger
/// summary handler, the ratio is the service the engine pays from, and "achieved" is
/// <see cref="QuotaAchievedQuery"/> — which says in its own comment that every dashboard handler must
/// call it, because a screen that summed credits itself once showed 671% against a true 336%.
/// </summary>
public sealed class GetMyDashboardHandler(
    IApplicationDbContext db,
    IAuthorizationService authorizationService,
    ICurrentUserService currentUser,
    IQuotaAttainmentService attainment,
    ISender sender,
    IClock clock)
    : IRequestHandler<GetMyDashboardQuery, Result<MyDashboardDto>>
{
    public async Task<Result<MyDashboardDto>> Handle(
        GetMyDashboardQuery request, CancellationToken cancellationToken)
    {
        // The same permission the ledger summary needs. Every role that can sign in holds it, so this
        // is a floor rather than a gate — the narrowing is done by WHOSE payee is resolved below.
        await authorizationService.RequireAsync(Permission.LedgerSummaryRead, cancellationToken);

        var userId = currentUser.UserId;
        if (string.IsNullOrEmpty(userId))
            return Result<MyDashboardDto>.Failure("Not authenticated.");

        var payee = await db.Payees
            .Where(p => p.UserId == userId)
            .Select(p => new { p.Id, p.FullName })
            .FirstOrDefaultAsync(cancellationToken);

        if (payee is null)
            return Result<MyDashboardDto>.Success(new MyDashboardDto(false, null, null, null, []));

        // ★ Routed through the existing handler rather than re-querying: it already splits the figures
        // that ARE period-scoped from the balance that cannot be, and that distinction is the one
        // people misread. PayeeAccessGuard runs inside it and will resolve to this same payee.
        var summary = await sender.Send(new GetPayeeLedgerSummaryQuery(payee.Id), cancellationToken);

        var quotas = await LoadQuotasAsync(payee.Id, cancellationToken);

        var awaitingSetup = await CountSalesAwaitingSetupAsync(payee.Id, cancellationToken);

        // ★ The id travels back so the screen can mount the ledger panel that already exists, rather
        // than grow a second rendering of the same movements. It is an ANSWER, not an input: nobody
        // can send one in, and the guard behind those endpoints still decides whether they may read
        // the one they were given.
        return Result<MyDashboardDto>.Success(new MyDashboardDto(
            true,
            payee.Id,
            payee.FullName,
            summary.IsSuccess ? summary.Value : null,
            quotas,
            awaitingSetup));
    }

    /// <summary>
    /// How many of this person's sales the engine cannot turn into commission yet (KAN-94).
    ///
    /// ★★ IT ASKS <see cref="UnprocessablePendingSpec"/> AND DOES NOT RE-DECIDE ANYTHING. That class is
    /// the single definition of "Pending and stuck, and why", and the administrator's two surfaces —
    /// the dashboard's "needs attention" card and the Reconciliation Centre — already read it. A second
    /// implementation here would be a second opinion about the same rows, and the two would drift: the
    /// rep would be told there is a problem on a day the admin's screen says there is none, or worse,
    /// the reverse. It also means closures made from the Centre silence this notice too, because the
    /// exclusion lives INSIDE the spec.
    ///
    /// ★★ TWO OF THE THREE REASONS, AND THE THIRD IS IMPOSSIBLE HERE. <c>NoPayee</c> selects rows whose
    /// PayeeId is null; this query is anchored on a resolved payee, so it can never match. Counting it
    /// would be dead code that reads as thoroughness.
    ///
    /// ★ THE TWO COUNTS ARE SUMMED BECAUSE THE SPEC GUARANTEES THEY ARE DISJOINT — it splits Pending
    /// rows "into mutually-exclusive primary reasons", and the conditions prove it: NoActiveAssignment
    /// requires that NO covering assignment exists, CurrencyMismatch requires that one does. A row
    /// cannot satisfy both, so nothing is double-counted.
    /// </summary>
    private async Task<int> CountSalesAwaitingSetupAsync(Guid payeeId, CancellationToken cancellationToken)
    {
        var noAssignment = await UnprocessablePendingSpec
            .NoActiveAssignment(db)
            .CountAsync(t => t.PayeeId == payeeId, cancellationToken);

        var currencyMismatch = await UnprocessablePendingSpec
            .CurrencyMismatch(db)
            .CountAsync(t => t.PayeeId == payeeId, cancellationToken);

        return noAssignment + currencyMismatch;
    }

    /// <summary>
    /// The person's quotas in effect today, and how far along each one is.
    ///
    /// ★★ IT SELECTS THE SAME QUOTA THE ENGINE DOES. Periods of one plan can overlap, and
    /// <c>QuotaAttainmentService</c> breaks the tie by shortest span then most recent — so listing a
    /// different one would print a target the ratio next to it was never measured against. The status
    /// filter is the engine's too: anything but Draft.
    ///
    /// ★ THE PERIOD IS FILTERED IN MEMORY ON PURPOSE. EF Core 8 does not reliably translate DateOnly
    /// comparisons on the owned <c>DateRange</c>; the engine's own lookup says so and does the same. A
    /// payee has a handful of quotas, so the rows never justify fighting the translation.
    /// </summary>
    private async Task<IReadOnlyList<MyQuotaAttainmentDto>> LoadQuotasAsync(
        Guid payeeId, CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(clock.UtcNow);

        var quotas = await db.Quotas
            .Where(q => q.PayeeId == payeeId && q.Status != QuotaStatus.Draft)
            .ToListAsync(cancellationToken);

        var inEffect = quotas
            .Where(q => q.Period.Start <= today && q.Period.End >= today)
            .GroupBy(q => q.PlanId)
            .Select(g => g
                .OrderBy(q => q.Period.End.DayNumber - q.Period.Start.DayNumber)
                .ThenByDescending(q => q.CreatedAt)
                .First())
            .ToList();

        if (inEffect.Count == 0) return [];

        var planIds = inEffect.Select(q => q.PlanId).ToList();
        var planNames = await db.CompensationPlans
            .Where(p => planIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => p.Name, cancellationToken);

        var result = new List<MyQuotaAttainmentDto>(inEffect.Count);

        foreach (var q in inEffect)
        {
            var reading = await attainment.ComputeAsync(payeeId, q.PlanId, today, cancellationToken);
            var achieved = await AchievedAsync(payeeId, q, cancellationToken);

            result.Add(new MyQuotaAttainmentDto(
                q.Id,
                planNames.TryGetValue(q.PlanId, out var name) ? name : string.Empty,
                q.Amount.Amount,
                q.Amount.Currency,
                q.MeasurementType.ToString(),
                achieved,
                reading.Value.Value,
                // ★ The code, not a sentence: the screen decides how to say "nobody set a target" in
                // the reader's language, and a new member here must not need a redeploy to translate.
                reading.Source.ToString(),
                q.Period.Start,
                q.Period.End));
        }

        return result;
    }

    /// <summary>
    /// ★ THE ONE DEFINITION OF "ACHIEVED", called rather than reimplemented — and a Units quota counts
    /// deals, not money, so it must not be summed in a currency.
    /// </summary>
    private Task<decimal> AchievedAsync(Guid payeeId, Quota quota, CancellationToken ct)
        => quota.MeasurementType == QuotaMeasurementType.Units
            ? QuotaAchievedQuery.UnitsAsync(
                db, payeeId, quota.PlanId, quota.Period.Start, quota.Period.End, ct)
            : QuotaAchievedQuery.RevenueAsync(
                db, payeeId, quota.PlanId, quota.Period.Start, quota.Period.End,
                quota.Amount.Currency, ct);
}
