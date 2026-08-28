using MediatR;
using Wasnie.Application.Compensation.DTOs;
using Wasnie.Domain.Common.Results;

namespace Wasnie.Application.Compensation.Queries.Plans;

/// <summary>
/// What every active rule of a SAVED plan would pay for one hypothetical transaction.
///
/// ★★ EVERY RULE IN ONE CALL, ON PURPOSE. The question people actually ask is "how does each rule
/// calculate this?", and answering it with one round trip per rule gives a language model three
/// separate results to keep straight — which is three chances to attribute rule 2's number to rule 3.
/// One call, one ordered list, each entry naming its own rule.
///
/// ★ IT DIFFERS FROM <c>SimulateRuleQuery</c> IN WHAT IT TRUSTS. That one carries a definition typed
/// into a form and therefore has to validate it as if it were being saved. This one names a plan that
/// is already stored, so its rules passed validation when they were written; what it adds instead is
/// resolving the plan by name.
/// </summary>
/// <param name="Quantity">
/// Units for a Units-measured rule. Rules measured on revenue ignore it, which is why one call can
/// mix both: the same transaction has an amount AND a count.
/// </param>
public sealed record SimulatePlanRulesQuery(
    Guid? PlanId,
    string? PlanName,
    decimal Amount,
    int Quantity = 1,
    decimal? AttainmentPct = null,
    decimal? PriorCumulative = null,
    decimal? QuotaTarget = null) : IRequest<Result<PlanSimulationDto>>;
