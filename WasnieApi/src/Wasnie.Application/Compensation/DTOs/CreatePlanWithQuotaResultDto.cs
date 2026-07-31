namespace Wasnie.Application.Compensation.DTOs;

/// <summary>
/// Why one quota of the request could not be created. Carries the POSITION in the submitted list as
/// well as the payee, because the same payee may legitimately appear twice and "the second one" is
/// then the only way to point at the offender.
/// </summary>
/// <param name="PayeeName">
/// Empty when the id matches no payee in this tenant — which is itself the useful signal.
/// </param>
public sealed record PlanQuotaFailureDto(int Index, Guid PayeeId, string PayeeName, string Reason);

/// <summary>
/// Outcome of creating a plan together with its quotas. Exactly ONE side is populated, because the
/// operation is all-or-nothing: either the plan and every quota exist, or nothing does and every
/// reason is listed.
///
/// There is deliberately no "plan created, quota failed" shape to represent. That state is the exact
/// failure this command exists to prevent — a plan with an accelerator and no quota pays zero without
/// erroring — so making it unrepresentable in the response is not decoration, it is the contract.
/// </summary>
public sealed record CreatePlanWithQuotaResultDto(
    PlanDto? Plan,
    IReadOnlyList<QuotaSummaryDto> Quotas,
    IReadOnlyList<PlanQuotaFailureDto> Failures)
{
    public bool IsSuccess => Failures.Count == 0;
    public int QuotaCount => Quotas.Count;
}
