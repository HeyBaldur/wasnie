using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Compensation.DTOs;
using Wasnie.Application.Compensation.Handlers.Payouts;
using Wasnie.Domain.Compensation.Payouts;

namespace Wasnie.Application.Compensation.Common;

/// <summary>
/// Turns payouts into the two sheets every accounting export shares: one row per payee, and one row
/// per commission line behind those totals.
///
/// ★★ IT IS SHARED BECAUSE THREE BUTTONS ASK THE SAME QUESTION. The payouts list, the pay-run detail
/// and the pay-runs list all end up handing somebody a file they will pay from. They used to answer at
/// three different levels of detail — and the pay-runs one answered with four rows that named nobody,
/// which is how an administrator discovered they could not tell who had been paid.
///
/// ★ THE ONE RULE THAT MUST NOT BE COPIED IS THE PAYMENT STATE, and it is not: it comes from
/// <see cref="GetPayoutByIdHandler.ResolvePaymentState"/>, the same method the screen uses. A file
/// that called a line unpaid while the screen called it paid would leave nobody able to say which to
/// believe.
///
/// ★ IT IS BATCHED. <c>GetPayoutByIdHandler.BuildLinesAsync</c> costs four queries per payout and maps
/// the whole calculation snapshot; over a 200-payee run that is eight hundred round trips of work this
/// sheet does not display. This does three queries whatever the size.
/// </summary>
public static class PayoutExportProjection
{
    /// <summary>One row per payout: the level somebody actually pays from.</summary>
    public static List<PayoutExportRow> BuildSummary(
        IReadOnlyList<CompensationPayout> payouts,
        IReadOnlyDictionary<Guid, string> planNames) =>
        payouts.Select(p => new PayoutExportRow(
            Id: p.Id,
            PayeeName: p.PayeeSnapshot.FullName,
            PayeeCode: p.PayeeSnapshot.EmployeeCode,
            PlanName: PlanNameOf(planNames, p.PlanId),
            PeriodStart: p.Period.Start,
            PeriodEnd: p.Period.End,
            TotalCommissionAmount: p.TotalCommission.Amount,
            TotalCommissionCurrency: p.TotalCommission.Currency,
            Status: p.Status.ToString(),
            CalculatedAt: p.CalculatedAt,
            UpdatedAt: p.UpdatedAt)).ToList();

    /// <summary>
    /// One row per commission line. The payouts must have been loaded with their <c>Lines</c>.
    /// </summary>
    public static async Task<List<PayoutDetailExportRow>> BuildDetailAsync(
        IApplicationDbContext db,
        IReadOnlyList<CompensationPayout> payouts,
        IReadOnlyDictionary<Guid, string> planNames,
        CancellationToken cancellationToken)
    {
        var creditIds = payouts.SelectMany(p => p.Lines).Select(l => l.CreditId).Distinct().ToList();
        if (creditIds.Count == 0) return [];

        var credits = await db.Credits
            .Where(c => creditIds.Contains(c.Id))
            .AsNoTracking()
            .Select(c => new { c.Id, c.TransactionId, c.ConsumedByPayoutId })
            .ToListAsync(cancellationToken);
        var creditById = credits.ToDictionary(c => c.Id);

        var txIds = credits.Select(c => c.TransactionId).Distinct().ToList();
        var txById = await db.CompensationTransactions
            .Where(t => txIds.Contains(t.Id))
            .Select(t => new { t.Id, t.ReferenceNumber, t.Description, t.TransactionDate })
            .ToDictionaryAsync(t => t.Id, cancellationToken);

        var detail = new List<PayoutDetailExportRow>();

        foreach (var payout in payouts)
        {
            var planName = PlanNameOf(planNames, payout.PlanId);

            foreach (var line in payout.Lines)
            {
                creditById.TryGetValue(line.CreditId, out var credit);
                var tx = credit is not null ? txById.GetValueOrDefault(credit.TransactionId) : null;

                detail.Add(new PayoutDetailExportRow(
                    PayoutId: payout.Id,
                    PayeeName: payout.PayeeSnapshot.FullName,
                    PayeeCode: payout.PayeeSnapshot.EmployeeCode,
                    PlanName: planName,
                    PeriodStart: payout.Period.Start,
                    PeriodEnd: payout.Period.End,
                    TransactionReference: tx?.ReferenceNumber,
                    TransactionDate: tx?.TransactionDate,
                    TransactionDescription: tx?.Description,
                    BaseAmount: line.BaseAmount.Amount,
                    BaseCurrency: line.BaseAmount.Currency,
                    RuleName: line.RuleName,
                    CommissionAmount: line.CommissionAmount.Amount,
                    CommissionCurrency: line.CommissionAmount.Currency,
                    PaymentState: GetPayoutByIdHandler
                        .ResolvePaymentState(credit?.ConsumedByPayoutId, payout.Id).ToString(),
                    PayoutStatus: payout.Status.ToString()));
            }
        }

        return detail;
    }

    /// <summary>Plan names for a set of payouts, in one query.</summary>
    public static async Task<Dictionary<Guid, string>> LoadPlanNamesAsync(
        IApplicationDbContext db,
        IReadOnlyList<CompensationPayout> payouts,
        CancellationToken cancellationToken)
    {
        var planIds = payouts.Select(p => p.PlanId).Distinct().ToList();
        if (planIds.Count == 0) return [];

        return await db.CompensationPlans
            .Where(p => planIds.Contains(p.Id))
            .Select(p => new { p.Id, p.Name })
            .ToDictionaryAsync(p => p.Id, p => p.Name, cancellationToken);
    }

    // A deleted plan must not blank the column: the short id is at least traceable, and a blank cell
    // in a file somebody pays from reads as "no plan" rather than "name unavailable".
    private static string PlanNameOf(IReadOnlyDictionary<Guid, string> names, Guid planId) =>
        names.TryGetValue(planId, out var n) ? n : planId.ToString("N")[..8];
}
