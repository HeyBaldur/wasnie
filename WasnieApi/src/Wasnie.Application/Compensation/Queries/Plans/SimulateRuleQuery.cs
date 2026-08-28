using MediatR;
using Wasnie.Application.Compensation.DTOs;
using Wasnie.Domain.Common.Results;
using Wasnie.Domain.Compensation.Rules;

namespace Wasnie.Application.Compensation.Queries.Plans;

/// <summary>
/// Run one rule over one hypothetical transaction and report what it would pay, step by step.
///
/// ★★ IT CARRIES THE RULE'S DEFINITION, NOT ITS ID, AND THAT IS THE WHOLE DESIGN. The screen this
/// serves is a live preview of a FORM, and that form is used to create rules as well as edit them.
/// Simulating by id would leave the card dead while creating (there is no id yet) and, while
/// editing, would put the rate the user just typed next to a figure computed from the rate still in
/// the database — two contradictory numbers in one card, which is precisely the loss of trust the
/// card exists to prevent.
///
/// ★ IT IS A QUERY, AND IT WRITES NOTHING. No credit, no ledger entry, no counter. The engine's
/// core is pure; the rule built from this definition is an in-memory domain object that never meets
/// the DbContext.
/// </summary>
/// <param name="PlanId">
/// The plan the rule belongs to. Loaded through the tenant-filtered DbSet, so it both supplies the
/// currency and is what stops a caller simulating against somebody else's plan.
/// </param>
/// <param name="AttainmentPct">
/// ★ Optional, and its absence is meaningful. Null against an attainment table does NOT fall back
/// on the engine's 1.0 default — the query refuses to answer instead. See
/// <see cref="RuleSimulationBlocker.AttainmentContextRequired"/>.
/// </param>
public sealed record SimulateRuleQuery(
    Guid PlanId,
    string Name,
    Measurement Measurement,
    RateTable RateTable,
    Trigger? Trigger,
    Modifier? Modifier,
    Cap? Cap,
    Floor? Floor,
    decimal Amount,
    int Quantity,
    decimal? AttainmentPct = null,
    decimal? PriorCumulative = null,
    decimal? QuotaTarget = null) : IRequest<Result<RuleSimulationDto>>;
