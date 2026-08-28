using MediatR;
using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Common.Abstractions;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Compensation.Calculation;
using Wasnie.Application.Compensation.DTOs;
using Wasnie.Application.Compensation.Queries.Plans;
using Wasnie.Domain.Authorization;
using Wasnie.Domain.Common.Results;
using Wasnie.Domain.Compensation.Enums;
using Wasnie.Domain.Compensation.Plans;
using Wasnie.Domain.Compensation.Transactions;
using Wasnie.Domain.Compensation.ValueObjects;
using Wasnie.Domain.Exceptions;

namespace Wasnie.Application.Compensation.Handlers.Plans;

/// <summary>
/// Runs the real engine over a hypothetical transaction and hands back its own trace.
///
/// ★★ IT COMPUTES NOTHING ITSELF. Every figure below comes from
/// <see cref="IRuleCalculationExplainer"/>, which asks the same method the pay run asks. The moment
/// this handler started doing arithmetic there would be two commission engines, and the one people
/// look at before saving a plan would be the one that is wrong.
/// </summary>
public sealed class SimulateRuleHandler(
    IApplicationDbContext db,
    IAuthorizationService authorizationService,
    IRuleCalculationExplainer explainer,
    IGuidGenerator guidGenerator,
    IClock clock)
    : IRequestHandler<SimulateRuleQuery, Result<RuleSimulationDto>>
{
    public async Task<Result<RuleSimulationDto>> Handle(
        SimulateRuleQuery request, CancellationToken cancellationToken)
    {
        // Reading a rule's behaviour is reading the plan. Same permission the screen already needs.
        await authorizationService.RequireAsync(Permission.PlansRead, cancellationToken);

        // ★ THE TENANT BOUNDARY IS THIS QUERY, not a check further down. CompensationPlans carries a
        // global query filter, so a plan belonging to another tenant simply is not found — there is
        // no branch here that could be forgotten.
        var plan = await db.CompensationPlans
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == request.PlanId, cancellationToken);

        if (plan is null)
        {
            return Result<RuleSimulationDto>.Failure("Plan not found.");
        }

        // ★ THE SAME GUARD THE SAVE PATH APPLIES (AddRuleToPlanHandler / UpdateRuleHandler). A rule
        // the system would refuse to store must not be simulable either, or the preview answers for
        // a configuration that can never exist.
        if (request.Cap is not null && request.Cap.Scope != CapScope.PerTransaction)
        {
            return Result<RuleSimulationDto>.Failure(
                "Only Per Transaction cap scope is currently supported.");
        }

        if (request.Amount < 0m)
        {
            return Result<RuleSimulationDto>.Failure("Amount must not be negative.");
        }

        if (request.Quantity < 1)
        {
            return Result<RuleSimulationDto>.Failure("Quantity must be at least 1.");
        }

        // ── ★ The context the engine would otherwise invent ──────────────────
        //
        // ★★ THIS IS THE REFUSAL THE WHOLE FEATURE TURNS ON. An attainment table needs to know how
        // much of their quota the rep has reached, and the engine's default when nobody says is
        // 1.0 — a rep at full quota. Simulating anyway would not fail loudly; it would answer
        // confidently with one particular rep's commission and present it as anybody's. So the
        // query declines and says which context is missing.
        // ★ THE SHARED DECISION, not a copy of it. The assistant's tool asks the same question, and a
        // chat that answers where this screen refuses would be worse than either behaviour alone.
        var blocker = RuleSimulationContext.BlockerFor(
            request.RateTable, request.AttainmentPct, request.PriorCumulative, request.QuotaTarget);
        if (blocker != RuleSimulationBlocker.None)
        {
            return Result<RuleSimulationDto>.Success(new RuleSimulationDto(
                Simulated: false,
                Blocker: blocker,
                CreditGenerated: false,
                CommissionAmount: null,
                Currency: plan.Currency,
                Steps: []));
        }

        // ── The definition, validated by the domain that would have stored it ─
        Rule rule;
        try
        {
            rule = BuildRule(request, plan.TenantId, plan.Currency);
        }
        catch (DomainException ex)
        {
            // ★ NOT A RE-IMPLEMENTATION OF THE RULES — the rule is built through the very same
            // Plan.AddRule the save path calls, on a throwaway in-memory plan that never meets the
            // DbContext. Every invariant that would reject this rule on save rejects it here, and
            // there is no second copy of them to drift.
            return Result<RuleSimulationDto>.Failure(ex.Message);
        }

        var transaction = BuildTransaction(request, plan.TenantId, plan.Currency);

        var splitContext = request.PriorCumulative.HasValue && request.QuotaTarget.HasValue
            ? new AttainmentSplitContext(request.PriorCumulative.Value, request.QuotaTarget.Value)
            : null;

        var trace = explainer.Explain(
            rule, transaction, plan.Currency, request.AttainmentPct, splitContext);

        return Result<RuleSimulationDto>.Success(new RuleSimulationDto(
            Simulated: true,
            Blocker: RuleSimulationBlocker.None,
            CreditGenerated: trace.CreditGenerated,
            CommissionAmount: trace.Commission?.Amount,
            Currency: plan.Currency,
            Steps: trace.Steps.Select(ToDto).ToList()));
    }

    /// <summary>
    /// Builds the rule the way the save path builds it, on a plan that exists for the length of this
    /// method. Nothing here is tracked, added or saved.
    /// </summary>
    private Rule BuildRule(SimulateRuleQuery request, Guid tenantId, string currency)
    {
        var now = clock.UtcNowOffset;

        var scratch = Plan.Create(
            tenantId: tenantId,
            name: "simulation",
            description: "simulation",
            effectivePeriod: DateRange.Of(
                DateOnly.FromDateTime(now.UtcDateTime.Date),
                DateOnly.FromDateTime(now.UtcDateTime.Date)),
            currency: currency,
            createdBy: "simulation",
            id: guidGenerator.NewGuid(),
            now: now,
            eventId: guidGenerator.NewGuid());

        return scratch.AddRule(
            name: string.IsNullOrWhiteSpace(request.Name) ? "simulation" : request.Name,
            sortOrder: 0,
            measurement: request.Measurement,
            rateTable: request.RateTable,
            trigger: request.Trigger,
            modifier: request.Modifier,
            cap: request.Cap,
            floor: request.Floor);
    }

    /// <summary>
    /// The hypothetical transaction. A domain factory call, not a row: <c>Ingest</c> constructs and
    /// validates an object and persists nothing.
    /// </summary>
    private CompensationTransaction BuildTransaction(
        SimulateRuleQuery request, Guid tenantId, string currency)
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
