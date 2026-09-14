using MediatR;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Domain.Audit;
using Wasnie.Domain.Common.Results;

namespace Wasnie.Application.Compensation.Commands.Plans;

/// <summary>
/// Permanently delete a Draft plan and its rules (KAN-69).
///
/// ★ MONEY-CRITICAL ON PURPOSE. It writes no money, but it is the one command that REMOVES a row, and
/// the audit entry is the only evidence left afterwards. Through <c>IMoneyCriticalCommand</c> the delete
/// and its audit row commit in the same transaction: if the audit write fails, the plan is not deleted.
/// </summary>
public sealed record DeletePlanCommand(Guid PlanId) : IRequest<Result>, IMoneyCriticalCommand
{
    /// <summary>
    /// What was deleted, recorded by the handler just before removing it. ★ READ AFTER THE HANDLER RUNS
    /// (see <see cref="IAuditableCommand.AuditMetadata"/>): once the plan is gone, nothing else can say
    /// which plan this id was. Null when the handler refused — and then no audit row is written anyway.
    /// </summary>
    public DeletedPlanFacts? Deleted { get; internal set; }

    public string AuditAction => AuditActions.PlanDeleted;
    public string AuditResourceType => ResourceTypes.Plan;
    public string? AuditResourceId => PlanId.ToString();
    public string? AuditDisplayName => Deleted?.Name;

    public Dictionary<string, string>? AuditMetadata => Deleted is null
        ? null
        : new()
        {
            ["name"] = Deleted.Name,
            ["version"] = Deleted.Version.ToString(),
            ["currency"] = Deleted.Currency,
            ["effectiveStart"] = Deleted.EffectiveStart.ToString("yyyy-MM-dd"),
            ["effectiveEnd"] = Deleted.EffectiveEnd.ToString("yyyy-MM-dd"),
            ["ruleCount"] = Deleted.RuleCount.ToString(),
        };
}

public sealed record DeletedPlanFacts(
    string Name, int Version, string Currency, DateOnly EffectiveStart, DateOnly EffectiveEnd, int RuleCount);
