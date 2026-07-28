using MediatR;
using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Compensation.DTOs;
using Wasnie.Application.Compensation.Queries.Ledger;
using Wasnie.Domain.Authorization;
using Wasnie.Domain.Common.Results;

namespace Wasnie.Application.Compensation.Handlers.Ledger;

/// <summary>
/// Builds the account statement the screen draws. EVERY money figure is finished here — the client
/// formats and nothing else.
///
/// The source of the cash-flow numbers is <c>PayRunSettlement</c>, the row written when the run was
/// actually paid. Nothing is recomputed from payouts or credits: if the settlement says 500 was
/// withheld, that is what left the company, and a second calculation could only disagree with it.
/// </summary>
public sealed class GetPayeeStatementHandler(
    IApplicationDbContext db,
    IAuthorizationService authorizationService)
    : IRequestHandler<GetPayeeStatementQuery, Result<IReadOnlyList<PayeeStatementDto>>>
{
    public async Task<Result<IReadOnlyList<PayeeStatementDto>>> Handle(
        GetPayeeStatementQuery request, CancellationToken cancellationToken)
    {
        await authorizationService.RequireAsync(Permission.LedgerRead, cancellationToken);

        var payee = await db.Payees
            .Where(p => p.Id == request.PayeeId)
            .Select(p => new { p.Id, p.FullName })
            .FirstOrDefaultAsync(cancellationToken);

        if (payee is null)
            return Result<IReadOnlyList<PayeeStatementDto>>.Failure("Payee not found.");

        var balances = await db.PayeeBalances
            .Where(b => b.PayeeId == request.PayeeId)
            .ToListAsync(cancellationToken);

        if (balances.Count == 0)
            return Result<IReadOnlyList<PayeeStatementDto>>.Success([]);

        // The most recent settlement per currency — the run whose numbers the header explains.
        var settlements = await db.PayRunSettlements
            .Where(s => s.PayeeId == request.PayeeId)
            .OrderByDescending(s => s.AppliedAt)
            .ToListAsync(cancellationToken);

        // Caps of the plans that fed each settled run, so the explanatory sentence can name a
        // percentage only when there IS a single one to name.
        var capsByRun = await BuildCapsByRunAsync(
            request.PayeeId, settlements.Select(s => s.PayRunId).Distinct().ToList(), cancellationToken);

        var statements = new List<PayeeStatementDto>();

        foreach (var balance in balances.OrderBy(b => b.Currency))
        {
            var latest = settlements.FirstOrDefault(s => s.Currency == balance.Currency);

            if (latest is null)
            {
                // Debt exists but no run has settled against it yet: the cash-flow half is all zeros
                // and the balance half shows the standing debt on both sides.
                statements.Add(new PayeeStatementDto(
                    payee.Id, payee.FullName, balance.Currency,
                    CommissionsThisPeriod: 0m, RetentionApplied: 0m, NetPayable: 0m,
                    PreviousDebt: balance.Balance.Amount,
                    Amortization: 0m,
                    NewCarryover: balance.Balance.Amount,
                    CapPercentApplied: null, CapLimited: false,
                    PayRunId: null, SettledAt: null));
                continue;
            }

            // Previous debt is the settlement's own arithmetic, read back rather than re-derived from
            // the live balance: a manual adjustment made after the run would otherwise rewrite history.
            var previousDebt = -(latest.ClawbackWithheld.Amount + latest.CarryoverRemaining.Amount);
            var capLimited = latest.CarryoverRemaining.Amount > 0m && latest.NetPaid.Amount > 0m;

            capsByRun.TryGetValue((latest.PayRunId, balance.Currency), out var cap);

            statements.Add(new PayeeStatementDto(
                payee.Id, payee.FullName, balance.Currency,
                CommissionsThisPeriod: latest.GrossCommission.Amount,
                RetentionApplied: latest.ClawbackWithheld.Amount,
                NetPayable: latest.NetPaid.Amount,
                PreviousDebt: previousDebt,
                Amortization: latest.ClawbackWithheld.Amount,
                NewCarryover: -latest.CarryoverRemaining.Amount,
                CapPercentApplied: cap,
                CapLimited: capLimited,
                PayRunId: latest.PayRunId,
                SettledAt: latest.AppliedAt));
        }

        return Result<IReadOnlyList<PayeeStatementDto>>.Success(statements);
    }

    /// <summary>
    /// A single cap per (run, currency) when every plan that paid the payee in that run shares it;
    /// null otherwise. Naming one plan's percentage while another plan used a different one would be
    /// a wrong number in a sentence explaining someone's pay.
    /// </summary>
    private async Task<Dictionary<(Guid, string), decimal?>> BuildCapsByRunAsync(
        Guid payeeId, List<Guid> payRunIds, CancellationToken ct)
    {
        var result = new Dictionary<(Guid, string), decimal?>();
        if (payRunIds.Count == 0) return result;

        var payouts = await db.CompensationPayouts
            .Where(p => p.PayeeId == payeeId && p.PayRunId != null && payRunIds.Contains(p.PayRunId!.Value))
            .Select(p => new { RunId = p.PayRunId!.Value, p.PlanId, Currency = p.TotalCommission.Currency })
            .ToListAsync(ct);

        if (payouts.Count == 0) return result;

        var planIds = payouts.Select(p => p.PlanId).Distinct().ToList();
        var capByPlan = await db.CompensationPlans
            .Where(p => planIds.Contains(p.Id))
            .Select(p => new { p.Id, p.ClawbackCapPercent })
            .ToDictionaryAsync(p => p.Id, p => p.ClawbackCapPercent, ct);

        foreach (var group in payouts.GroupBy(p => (p.RunId, p.Currency)))
        {
            var caps = group
                .Select(p => capByPlan.TryGetValue(p.PlanId, out var c) ? c : null)
                .Distinct()
                .ToList();

            result[group.Key] = caps.Count == 1 ? caps[0] : null;
        }

        return result;
    }
}
