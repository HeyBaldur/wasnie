using MediatR;
using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Compensation.DTOs;
using Wasnie.Application.Compensation.Queries.Payouts;
using Wasnie.Domain.Authorization;
using Wasnie.Domain.Common.Results;
using Wasnie.Domain.Compensation.Payouts;
using Wasnie.Domain.Compensation.Rules;
using Wasnie.Domain.Compensation.ValueObjects;

namespace Wasnie.Application.Compensation.Handlers.Payouts;

public sealed class GetPayoutByIdHandler(
    IApplicationDbContext db,
    IAuthorizationService authorizationService)
    : IRequestHandler<GetPayoutByIdQuery, Result<PayoutDto>>
{
    public async Task<Result<PayoutDto>> Handle(
        GetPayoutByIdQuery request, CancellationToken cancellationToken)
    {
        await authorizationService.RequireAsync(Permission.PayoutsRead, cancellationToken);

        var payout = await db.CompensationPayouts
            .Include(p => p.Lines)
            .FirstOrDefaultAsync(p => p.Id == request.Id, cancellationToken);

        if (payout is null)
            return Result<PayoutDto>.Failure("Payout not found.");

        var planName = await db.CompensationPlans
            .Where(p => p.Id == payout.PlanId)
            .Select(p => p.Name)
            .FirstOrDefaultAsync(cancellationToken) ?? string.Empty;

        var lines = await BuildLinesAsync(payout.Lines, db, payout.Id, cancellationToken);

        var dto = new PayoutDto(
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
            CalculatedBy: payout.CalculatedBy,
            UpdatedAt: payout.UpdatedAt,
            UpdatedBy: payout.UpdatedBy,
            Lines: lines);

        return Result<PayoutDto>.Success(dto);
    }

    // Resolves source transactions + calculation snapshots for all lines.
    // Uses two bulk queries (no N+1). Credits loaded fully (AsNoTracking) to access
    // RuleSnapshot value-converted JSON without projection ambiguity.
    /// <param name="thisPayoutId">
    /// The payout these lines belong to. ★ REQUIRED TO TELL "paid here" FROM "paid somewhere else":
    /// after this payout is itself paid, every one of its credits carries a ConsumedByPayoutId, and
    /// without the comparison every line would announce itself as a duplicate of itself.
    /// </param>
    public static async Task<List<PayoutLineDto>> BuildLinesAsync(
        IReadOnlyList<PayoutLine> lines,
        IApplicationDbContext db,
        Guid thisPayoutId,
        CancellationToken ct)
    {
        var creditIds = lines.Select(l => l.CreditId).Distinct().ToList();

        var creditById = await db.Credits
            .Where(c => creditIds.Contains(c.Id))
            .AsNoTracking()
            .ToDictionaryAsync(c => c.Id, ct);

        var transactionIds = creditById.Values
            .Select(c => c.TransactionId)
            .Distinct()
            .ToList();

        // The payouts that consumed these credits, for the period shown on the badge. Only the ones
        // that are NOT this payout: a line paid here is not a duplicate.
        var payerIds = creditById.Values
            .Where(c => c.ConsumedByPayoutId != null && c.ConsumedByPayoutId != thisPayoutId)
            .Select(c => c.ConsumedByPayoutId!.Value)
            .Distinct()
            .ToList();

        var payerById = payerIds.Count == 0
            ? []
            : await db.CompensationPayouts
                .Where(p => payerIds.Contains(p.Id))
                .Select(p => new { p.Id, Start = p.Period.Start, End = p.Period.End })
                .ToDictionaryAsync(p => p.Id, ct);

        var txById = await db.CompensationTransactions
            .Where(t => transactionIds.Contains(t.Id))
            .Select(t => new
            {
                t.Id,
                t.ReferenceNumber,
                t.Description,
                t.ExternalId,
                t.TransactionDate,
                AmountValue = t.Amount.Amount,
                AmountCurrency = t.Amount.Currency,
            })
            .ToDictionaryAsync(t => t.Id, ct);

        return lines.Select(l =>
        {
            creditById.TryGetValue(l.CreditId, out var credit);
            var tx = credit is not null ? txById.GetValueOrDefault(credit.TransactionId) : null;

            var calculation = credit is not null
                ? MapCalculation(credit.RuleSnapshot, l.AppliedModifiers)
                : null;

            // ★★ THREE OUTCOMES, DECIDED ONCE, HERE. Deriving them on the screen from "is the other
            // payout's id null?" is what produced the bug this replaced: a payout that had itself
            // paid these credits reported every line as UNPAID, because the id was null for the
            // honest reason that the payer was not somebody else.
            var consumedBy = credit?.ConsumedByPayoutId;
            var paymentState = consumedBy is null
                ? PayoutLinePaymentState.Unpaid
                : consumedBy == thisPayoutId
                    ? PayoutLinePaymentState.PaidByThisPayout
                    : PayoutLinePaymentState.PaidByAnotherPayout;

            var payerId = paymentState == PayoutLinePaymentState.PaidByAnotherPayout ? consumedBy : null;
            var payer = payerId is not null ? payerById.GetValueOrDefault(payerId.Value) : null;

            return new PayoutLineDto(
                Id: l.Id,
                CreditId: l.CreditId,
                RuleId: l.RuleId,
                RuleName: l.RuleName,
                BaseAmount: l.BaseAmount.Amount,
                BaseCurrency: l.BaseAmount.Currency,
                CommissionAmount: l.CommissionAmount.Amount,
                CommissionCurrency: l.CommissionAmount.Currency,
                TransactionId: tx?.Id,
                TransactionReference: tx?.ReferenceNumber,
                TransactionDescription: tx?.Description,
                TransactionExternalId: tx?.ExternalId,
                TransactionDate: tx?.TransactionDate,
                TransactionAmount: tx?.AmountValue,
                TransactionCurrency: tx?.AmountCurrency,
                Calculation: calculation,
                PaymentState: paymentState,
                PaidInPayoutId: payerId,
                PaidInPayoutPeriodStart: payer?.Start,
                PaidInPayoutPeriodEnd: payer?.End);
        }).ToList();
    }

    public static LineCalculationDto MapCalculation(
        RuleSnapshot snapshot,
        IReadOnlyList<ModifierApplication> modifiers)
    {
        return new LineCalculationDto(
            PlanVersion: snapshot.PlanVersion,
            FrozenAt: snapshot.FrozenAt,
            RateTable: MapRateTable(snapshot.RateTable, snapshot.Measurement.Type),
            Trigger: MapTrigger(snapshot.Trigger),
            Modifiers: modifiers.Select(m => new ModifierApplicationDto(
                ModifierName: m.ModifierName,
                FactorApplied: m.FactorApplied,
                AmountBefore: m.AmountBefore.Amount,
                AmountBeforeCurrency: m.AmountBefore.Currency,
                AmountAfter: m.AmountAfter.Amount,
                AmountAfterCurrency: m.AmountAfter.Currency)).ToList());
    }

    /// <summary>
    /// ★ THE MEASUREMENT COMES FROM THE SNAPSHOT, not from the rule as it stands today. A payout is
    /// a frozen record of how somebody was paid; reading the live rule would describe a plan that may
    /// have been edited since, on a statement about money that already moved.
    /// </summary>
    private static RateTableDto MapRateTable(
        RateTable rt, Wasnie.Domain.Compensation.Enums.MeasurementType measurement) => new(
        Type: rt.Type.ToString(),
        FlatRate: rt.FlatRate,
        Tiers: rt.Tiers?.Select(t => new RateTierDto(t.From, t.To, t.Rate)).ToList(),
        AttainmentTiers: rt.AttainmentTiers?
            .Select(t => new AttainmentTierDto(t.AttainmentFrom, t.AttainmentTo, t.Rate))
            .ToList(),
        MeasurementBase: Wasnie.Application.Assistant.Tools.PlanRuleSemantics.BaseOf(measurement).ToString());

    private static TriggerDto MapTrigger(Trigger trigger) => new(
        IsAlways: trigger.Conditions.Count == 0,
        LogicalOperator: trigger.LogicalOperator.ToString(),
        Conditions: trigger.Conditions.Select(c => new ConditionDto(
            Field: c.Field,
            Operator: c.Operator.ToString(),
            Value: c.Value.Set is { Count: > 0 }
                ? string.Join(", ", c.Value.Set)
                : c.Value.Raw)).ToList());
}
