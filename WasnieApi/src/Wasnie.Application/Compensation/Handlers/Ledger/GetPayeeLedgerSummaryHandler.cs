using MediatR;
using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Authorization;
using Wasnie.Application.Common.Abstractions;
using Wasnie.Application.Common.Helpers;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Compensation.DTOs;
using Wasnie.Application.Compensation.Queries.Ledger;
using Wasnie.Domain.Authorization;
using Wasnie.Domain.Common.Results;
using Wasnie.Domain.Compensation.Enums;

namespace Wasnie.Application.Compensation.Handlers.Ledger;

/// <summary>
/// Crosses the two halves of a payee's money: what they earned (CompensationPayout) and what they owe
/// (PayeeBalance). See <see cref="PayeeLedgerSummaryDto"/> for why reading either one alone is a bug.
///
/// ★★ THIS HANDLER IS A FACADE, AND THAT IS THE WHOLE POINT OF ITS PERMISSION. It requires exactly one
/// thing — <see cref="Permission.LedgerSummaryRead"/> — and NOT Payouts.Read, even though it reads
/// CompensationPayout two screens below. The caller is authorised to receive a FINISHED SUMMARY of one
/// payee; the crossing behind it runs with the application's own authority.
///
/// ★ WHY NOT JUST GRANT REPS Payouts.Read. That was the obvious fix and it is the wrong one. Payouts.Read
/// opens the raw payout rows, the pay-run screens and the overlap queries — the payroll surface — and
/// every one of those would then need its own filter to keep a rep inside their own data. A broad grant
/// patched at the edges leaks the first time somebody adds an endpoint and forgets the filter. Scoping
/// the permission to the SHAPE OF THE ANSWER instead removes that whole class of mistake: there is no
/// peripheral surface to forget, because the permission never opened one.
///
/// ★ WHAT "THE APPLICATION'S OWN AUTHORITY" ACTUALLY IS, concretely: <see cref="IApplicationDbContext"/>
/// carries no per-user authorisation — it carries the TENANT query filter. So reading payouts here is
/// scoped to the tenant and to the payee, and to nothing else. The user's rights are enforced by the two
/// checks at the top of Handle, not by the data access. Nothing is bypassed; the authorisation simply
/// lives where it can be read in one place.
///
/// ★ THE RESOURCE GUARD IS THE OTHER HALF AND IS NOT OPTIONAL. The permission says WHAT you may receive;
/// the guard says WHOSE. Now that Rep and Manager hold LedgerSummaryRead, the guard is the ONLY thing
/// standing between a rep and a colleague's balance — it stopped being defence in depth and became the
/// load-bearing check.
///
/// ★ NO MONEY MATH IN A COMPONENT, AND NONE IN A MODEL EITHER. Every figure is finished here — summed,
/// signed, netted and classified — because the consumer is a language model, and arithmetic left to it
/// is arithmetic that will eventually be wrong in a sentence somebody believes.
/// </summary>
public sealed class GetPayeeLedgerSummaryHandler(
    IApplicationDbContext db,
    IAuthorizationService authorizationService,
    IPayeeAccessGuard payeeAccessGuard,
    IClock clock)
    : IRequestHandler<GetPayeeLedgerSummaryQuery, Result<PayeeLedgerSummaryDto>>
{
    /// <summary>Statuses that mean "the plan rules produced this" — accrual, not cash.</summary>
    private static readonly CompensationPayoutStatus[] AccruedStatuses =
    [
        CompensationPayoutStatus.Calculated,
        CompensationPayoutStatus.Approved,
        CompensationPayoutStatus.Paid,
    ];

    /// <summary>Accrued but not yet paid — the money still coming.</summary>
    private static readonly CompensationPayoutStatus[] AwaitingStatuses =
    [
        CompensationPayoutStatus.Calculated,
        CompensationPayoutStatus.Approved,
    ];

    public async Task<Result<PayeeLedgerSummaryDto>> Handle(
        GetPayeeLedgerSummaryQuery request, CancellationToken cancellationToken)
    {
        // ONE permission, and deliberately not Payouts.Read — see the facade note above.
        await authorizationService.RequireAsync(Permission.LedgerSummaryRead, cancellationToken);

        // Access check BEFORE the lookup, so "no such payee" and "not yours" cannot be told apart.
        if (!await payeeAccessGuard.CanReadAsync(request.PayeeId, cancellationToken))
            return Result<PayeeLedgerSummaryDto>.Failure(PayeeAccessDenied.Message);

        var payee = await db.Payees
            .Where(p => p.Id == request.PayeeId)
            .Select(p => new { p.Id, p.FullName })
            .FirstOrDefaultAsync(cancellationToken);

        if (payee is null)
            return Result<PayeeLedgerSummaryDto>.Failure(PayeeAccessDenied.Message);

        var today = DateOnly.FromDateTime(clock.UtcNow);
        var (from, to) = PeriodHelper.ComputeDateRange(request.Period, today);

        // ── Earned in the window: accrual, by period INTERSECTION ────────────
        // Same predicate as the payouts screen's period filter (ListPayoutsHandler), so the assistant
        // and the screen cannot disagree about what "this month" contains.
        var accruedQuery = db.CompensationPayouts
            .Where(p => p.PayeeId == request.PayeeId && AccruedStatuses.Contains(p.Status));
        if (from.HasValue) accruedQuery = accruedQuery.Where(p => p.Period.End >= from.Value);
        if (to.HasValue) accruedQuery = accruedQuery.Where(p => p.Period.Start <= to.Value);

        var earned = await SumByCurrencyAsync(accruedQuery, cancellationToken);

        // ── Disputed in the window: same window, kept apart from earned ──────
        var disputedQuery = db.CompensationPayouts
            .Where(p => p.PayeeId == request.PayeeId && p.Status == CompensationPayoutStatus.Disputed);
        if (from.HasValue) disputedQuery = disputedQuery.Where(p => p.Period.End >= from.Value);
        if (to.HasValue) disputedQuery = disputedQuery.Where(p => p.Period.Start <= to.Value);

        var disputed = await SumByCurrencyAsync(disputedQuery, cancellationToken);

        // ── Paid in the window: CASH, by PaidAt CONTAINMENT ──────────────────
        // Deliberately a different predicate from the one above. Attributing cash by the compensation
        // period reports July's money in December; see PayoutsInPeriodRawAsync for the bug that taught
        // this. The upper bound covers the whole final day, since PaidAt is an instant.
        var paidQuery = db.CompensationPayouts
            .Where(p => p.PayeeId == request.PayeeId
                     && p.Status == CompensationPayoutStatus.Paid
                     && p.PaidAt != null);
        if (from.HasValue)
        {
            var fromInstant = new DateTimeOffset(
                from.Value.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc), TimeSpan.Zero);
            paidQuery = paidQuery.Where(p => p.PaidAt >= fromInstant);
        }
        if (to.HasValue)
        {
            var toInstant = new DateTimeOffset(
                to.Value.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc), TimeSpan.Zero);
            paidQuery = paidQuery.Where(p => p.PaidAt <= toInstant);
        }

        var paidOut = await SumByCurrencyAsync(paidQuery, cancellationToken);

        // ── Awaiting payment: ALL TIME, never window-scoped ──────────────────
        // Money earned last quarter and still unpaid is still owed today. Filtering this by the window
        // would hide exactly the case a person asks about.
        var awaiting = await SumByCurrencyAsync(
            db.CompensationPayouts.Where(p =>
                p.PayeeId == request.PayeeId && AwaitingStatuses.Contains(p.Status)),
            cancellationToken);

        // ── Debt: the materialised projection, as of now ─────────────────────
        // PayeeBalance is the running total the engine maintains in the same SaveChanges as every
        // entry, so this is a single row read per currency — not a scan of the ledger.
        var balances = await db.PayeeBalances
            .Where(b => b.PayeeId == request.PayeeId)
            .Select(b => new { b.Currency, b.Balance.Amount })
            .ToListAsync(cancellationToken);

        // Negative balance = debt; positive = an adjustment in the payee's favour, which is NOT a debt
        // and must not be reported as one.
        var debt = balances.ToDictionary(
            b => b.Currency,
            b => b.Amount < 0m ? -b.Amount : 0m,
            StringComparer.OrdinalIgnoreCase);

        // A credit balance is real money owed to the payee on top of their commissions, so it joins the
        // pending side rather than being silently dropped for having the wrong sign.
        var credit = balances.ToDictionary(
            b => b.Currency,
            b => b.Amount > 0m ? b.Amount : 0m,
            StringComparer.OrdinalIgnoreCase);

        var currencies = earned.Keys
            .Union(paidOut.Keys, StringComparer.OrdinalIgnoreCase)
            .Union(disputed.Keys, StringComparer.OrdinalIgnoreCase)
            .Union(awaiting.Keys, StringComparer.OrdinalIgnoreCase)
            .Union(debt.Keys, StringComparer.OrdinalIgnoreCase)
            .OrderBy(c => c, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var rows = currencies.Select(currency =>
        {
            var earnedAmount = Get(earned, currency);
            var awaitingAmount = Get(awaiting, currency) + Get(credit, currency);
            var debtAmount = Get(debt, currency);
            var net = awaitingAmount - debtAmount;

            return new PayeeCurrencyBalanceDto(
                Currency: currency,
                EarnedCommissionsInPeriod: earnedAmount,
                PaidOutInPeriod: Get(paidOut, currency),
                DisputedInPeriod: Get(disputed, currency),
                AwaitingPaymentAllTime: awaitingAmount,
                OutstandingDebt: debtAmount,
                NetPendingPayout: net,
                Interpretation: Classify(earnedAmount, awaitingAmount, debtAmount, net));
        }).ToList();

        return Result<PayeeLedgerSummaryDto>.Success(new PayeeLedgerSummaryDto(
            payee.Id,
            payee.FullName,
            PeriodLabel: request.Period,
            PeriodStart: from,
            PeriodEnd: to,
            ByCurrency: rows));
    }

    /// <summary>
    /// ★ THE ONE INFERENCE A LANGUAGE MODEL MUST NEVER MAKE: which zero this is. Decided from the
    /// numbers, in code, once — see <see cref="BalanceSemantic"/>.
    /// </summary>
    private static BalanceSemantic Classify(
        decimal earned, decimal awaiting, decimal debt, decimal net)
    {
        if (debt <= 0m)
            return earned > 0m || awaiting > 0m
                ? BalanceSemantic.EarningsAndNoDebt
                : BalanceSemantic.NothingRecorded;

        if (earned <= 0m && awaiting <= 0m)
            return BalanceSemantic.DebtOnly;

        return net < 0m ? BalanceSemantic.DebtExceedsPending : BalanceSemantic.EarningsWithDebt;
    }

    private static decimal Get(IReadOnlyDictionary<string, decimal> totals, string currency) =>
        totals.TryGetValue(currency, out var amount) ? amount : 0m;

    /// <summary>
    /// Sums in the DATABASE, grouped by currency. Never materialises the payouts: a payee with years of
    /// history must cost the same as one with a single cycle.
    /// </summary>
    private static async Task<Dictionary<string, decimal>> SumByCurrencyAsync(
        IQueryable<Domain.Compensation.Payouts.CompensationPayout> query, CancellationToken ct) =>
        await query
            .GroupBy(p => p.TotalCommission.Currency)
            .Select(g => new { Currency = g.Key, Total = g.Sum(p => p.TotalCommission.Amount) })
            .ToDictionaryAsync(x => x.Currency, x => x.Total, StringComparer.OrdinalIgnoreCase, ct);
}
