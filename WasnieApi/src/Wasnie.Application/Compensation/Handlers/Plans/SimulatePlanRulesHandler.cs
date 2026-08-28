using MediatR;
using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Common.Abstractions;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Assistant.Tools;
using Wasnie.Application.Compensation.Calculation;
using Wasnie.Application.Compensation.DTOs;
using Wasnie.Application.Compensation.Queries.Plans;
using Wasnie.Domain.Authorization;
using Wasnie.Domain.Common.Results;
using Wasnie.Domain.Compensation.Enums;
using Wasnie.Domain.Compensation.Plans;
using Wasnie.Domain.Compensation.Transactions;
using Wasnie.Domain.Compensation.ValueObjects;

namespace Wasnie.Application.Compensation.Handlers.Plans;

/// <summary>
/// Runs every active rule of a saved plan through the real engine for one hypothetical transaction.
///
/// ★★ IT COMPUTES NOTHING ITSELF, and that is the entire reason it exists. The assistant used to
/// answer "how much would this rule pay?" by doing the arithmetic in prose — 7,850 × 0.05 × 1.2 — and
/// got it right, which is worse than getting it wrong: a habit that produces correct answers until
/// the day the cascade has a cap in it. Every figure below comes from
/// <see cref="IRuleCalculationExplainer"/>, the same call the pay run makes.
///
/// ★ NOTHING IS WRITTEN. No credit, no ledger entry, no counter, no transaction row: the transaction
/// here is a domain object built in memory and never handed to the DbContext.
/// </summary>
public sealed class SimulatePlanRulesHandler(
    IApplicationDbContext db,
    IAuthorizationService authorizationService,
    IRuleCalculationExplainer explainer,
    IGuidGenerator guidGenerator,
    IClock clock)
    : IRequestHandler<SimulatePlanRulesQuery, Result<PlanSimulationDto>>
{
    public async Task<Result<PlanSimulationDto>> Handle(
        SimulatePlanRulesQuery request, CancellationToken cancellationToken)
    {
        await authorizationService.RequireAsync(Permission.PlansRead, cancellationToken);

        if (request.Amount < 0m) return Result<PlanSimulationDto>.Failure("Amount must not be negative.");
        if (request.Quantity < 1) return Result<PlanSimulationDto>.Failure("Quantity must be at least 1.");

        var plan = await ResolvePlanAsync(request, cancellationToken);
        if (plan is null) return Result<PlanSimulationDto>.Failure("Plan not found.");

        // Same ordering the plan screen shows, so "rule 2" means the same thing in both places.
        var rules = plan.Rules
            .Where(r => r.IsActive)
            .OrderBy(r => r.SortOrder)
            .ThenBy(r => r.Id)
            .ToList();

        var transaction = BuildTransaction(request, plan.TenantId, plan.Currency);

        var results = new List<PlanRuleSimulationDto>(rules.Count);
        foreach (var rule in rules)
        {
            // ★ THE SHARED REFUSAL, not a copy. The rule screen's simulator asks the same question of
            // the same helper, so the chat cannot produce a number where the screen declines to.
            var blocker = RuleSimulationContext.BlockerFor(
                rule.RateTable, request.AttainmentPct, request.PriorCumulative, request.QuotaTarget);

            if (blocker != RuleSimulationBlocker.None)
            {
                results.Add(new PlanRuleSimulationDto(
                    rule.Id, rule.Name, rule.SortOrder,
                    Simulated: false, Blocker: blocker,
                    CreditGenerated: false, CommissionAmount: null, Steps: []));
                continue;
            }

            var splitContext = request.PriorCumulative.HasValue && request.QuotaTarget.HasValue
                ? new AttainmentSplitContext(request.PriorCumulative.Value, request.QuotaTarget.Value)
                : null;

            var trace = explainer.Explain(
                rule, transaction, plan.Currency, request.AttainmentPct, splitContext);

            results.Add(new PlanRuleSimulationDto(
                rule.Id, rule.Name, rule.SortOrder,
                Simulated: true,
                Blocker: RuleSimulationBlocker.None,
                CreditGenerated: trace.CreditGenerated,
                // Null rather than zero when the trigger did not match: "this rule does not apply to
                // that deal" and "it applies and pays nothing" are different answers.
                CommissionAmount: trace.CreditGenerated ? trace.Commission?.Amount : null,
                Steps: trace.Steps.Select(ToDto).ToList()));
        }

        return Result<PlanSimulationDto>.Success(new PlanSimulationDto(
            plan.Id, plan.Name, plan.Currency, request.Amount, request.Quantity, results));
    }

    /// <summary>
    /// By id when the caller has one, otherwise by exact name.
    ///
    /// ★ THE TENANT BOUNDARY IS THE QUERY, not a check that could be forgotten: CompensationPlans
    /// carries a global filter, so another tenant's plan is simply not there — by id or by name.
    /// </summary>
    private async Task<Plan?> ResolvePlanAsync(SimulatePlanRulesQuery request, CancellationToken ct)
    {
        if (request.PlanId.HasValue)
        {
            return await db.CompensationPlans
                .AsNoTracking()
                .Include(p => p.Rules)
                .FirstOrDefaultAsync(p => p.Id == request.PlanId.Value, ct);
        }

        if (string.IsNullOrWhiteSpace(request.PlanName)) return null;

        // Loaded then matched in memory: the comparison folds the dash and space characters a model
        // substitutes into a title, which no SQL collation does. Same helper the other plan tools use.
        var candidates = await db.CompensationPlans
            .AsNoTracking()
            .Include(p => p.Rules)
            .ToListAsync(ct);

        return candidates.FirstOrDefault(p => PlanNameMatch.AreSame(p.Name, request.PlanName));
    }

    /// <summary>
    /// The hypothetical transaction: a domain factory call, not a row. <c>Ingest</c> constructs and
    /// validates an object and persists nothing.
    /// </summary>
    private CompensationTransaction BuildTransaction(
        SimulatePlanRulesQuery request, Guid tenantId, string currency)
    {
        var now = clock.UtcNowOffset;

        return CompensationTransaction.Ingest(
            tenantId: tenantId,
            referenceNumber: "SIMULATION",
            payeeId: guidGenerator.NewGuid(),
            amount: Money.Of(request.Amount, currency),
            transactionDate: DateOnly.FromDateTime(now.UtcDateTime.Date),
            source: TransactionSource.Manual,
            ingestedBy: "simulation",
            id: guidGenerator.NewGuid(),
            now: now,
            eventId: guidGenerator.NewGuid(),
            quantity: request.Quantity);
    }

    private static RuleSimulationStepDto ToDto(RuleCalculationStep step) => new(
        step.Component,
        step.Outcome,
        step.Input?.Amount,
        step.Output?.Amount,
        step.Operand,
        step.Threshold?.Amount,
        step.RateTable,
        step.AttainmentSource,
        step.Tiers?.Select(t => new RuleSimulationTierDto(
            t.From, t.To, t.Rate, t.Portion, t.Amount.Amount)).ToList());
}
