using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Common.Interfaces;

namespace Wasnie.Application.Compensation.Calculation;

/// <summary>
/// THE single definition of a quota's "achieved": the sum over the DISTINCT transactions that have a
/// live credit in (payee, plan) and fall inside the period. Deduped WITHIN the plan because Step 3 lets
/// several rules of one plan each credit the same sale — summing credits (the old <c>Credits join Tx</c>
/// shape) would count that sale once per rule and double the number.
///
/// Every attainment/achieved surface MUST call this — the motor's <c>QuotaAttainmentService</c> and every
/// profile/dashboard handler alike — so the number can never diverge again. That divergence is exactly
/// the bug this exists to prevent: a screen carrying its own credit-sum drifted from the motor and showed
/// 671% where the truth was 336%.
///
/// EXISTS over live credits ⇒ one SQL query per call, no join fan-out, no N+1. The dedup does NOT cross
/// plans: a sale credited in two different plans still counts once for EACH plan's quota (separate goals).
/// The one caller that can't use this per-(payee,plan) shape without an N+1 (the tenant-wide dashboard
/// average, which iterates every active quota) does the SAME dedup in-memory and is commented to say so.
/// </summary>
public static class QuotaAchievedQuery
{
    /// <summary>Revenue (Sales Quota): gross <c>Transaction.Amount</c> of each distinct matching sale.</summary>
    public static async Task<decimal> RevenueAsync(
        IApplicationDbContext db,
        Guid payeeId,
        Guid planId,
        DateOnly periodStart,
        DateOnly periodEnd,
        string quotaCurrency,
        CancellationToken ct)
    {
        var amounts = await db.CompensationTransactions
            .Where(t =>
                t.Amount.Currency == quotaCurrency &&
                t.TransactionDate >= periodStart &&
                t.TransactionDate <= periodEnd &&
                db.Credits.Any(c =>
                    c.TransactionId == t.Id &&
                    c.PayeeId == payeeId &&
                    c.PlanId == planId &&
                    c.SupersededAt == null))
            .Select(t => t.Amount.Amount)
            .ToListAsync(ct);

        return amounts.Sum();
    }

    /// <summary>Units: <c>Transaction.Quantity</c> of each distinct matching sale (no currency filter).</summary>
    public static async Task<decimal> UnitsAsync(
        IApplicationDbContext db,
        Guid payeeId,
        Guid planId,
        DateOnly periodStart,
        DateOnly periodEnd,
        CancellationToken ct)
    {
        var quantities = await db.CompensationTransactions
            .Where(t =>
                t.TransactionDate >= periodStart &&
                t.TransactionDate <= periodEnd &&
                db.Credits.Any(c =>
                    c.TransactionId == t.Id &&
                    c.PayeeId == payeeId &&
                    c.PlanId == planId &&
                    c.SupersededAt == null))
            .Select(t => t.Quantity)
            .ToListAsync(ct);

        return quantities.Sum();
    }
}
