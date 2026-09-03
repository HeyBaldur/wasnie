using MediatR;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Compensation.DTOs;
using Wasnie.Domain.Audit;
using Wasnie.Domain.Common.Results;

namespace Wasnie.Application.Compensation.Commands.Plans;

/// <summary>
/// Pull the emergency brake on one rule of a live plan.
///
/// ★ THE REASON IS PART OF THE COMMAND, NOT A NOTE ON THE SIDE. It is validated in the domain
/// (<c>Rule.Stop</c>) rather than by a FluentValidation rule, so the refusal arrives as a code the
/// browser can translate instead of an English sentence assembled in the backend.
/// </summary>
public sealed record StopRuleCommand(Guid PlanId, Guid RuleId, string? Reason)
    : IRequest<Result<RuleDto>>, IAuditableCommand
{
    public string AuditAction => AuditActions.PlanRuleStopped;
    public string AuditResourceType => ResourceTypes.Plan;
    public string? AuditResourceId => PlanId.ToString();
    public string? AuditDisplayName => null;

    // The reason lands in BOTH places on purpose: on the rule, because that is the surface a reader
    // actually opens, and here, because the audit row is what answers "who braked this, and when"
    // for a rule that a later clone-and-correct has already superseded.
    public Dictionary<string, string>? AuditMetadata => new()
    {
        ["ruleId"] = RuleId.ToString(),
        ["reason"] = Reason?.Trim() ?? string.Empty,
    };
}
