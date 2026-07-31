namespace Wasnie.Application.Compensation.DTOs;

/// <summary>
/// Why one payee of the batch could not receive the quota. Carries the NAME as well as the id: the
/// admin picked people, not GUIDs, and a list of GUIDs is not something anyone can act on.
/// </summary>
/// <param name="PayeeName">
/// Empty when the id matches no payee in this tenant — which is itself the useful signal.
/// </param>
public sealed record BulkQuotaFailureDto(Guid PayeeId, string PayeeName, string PayeeEmployeeCode, string Reason);

/// <summary>
/// Outcome of a bulk quota creation. Exactly ONE of the two lists is populated, because the operation
/// is all-or-nothing: either every quota was created, or none was and every reason is listed.
///
/// It is never "18 created, 2 failed". The domain permits duplicate/overlapping quotas, so a partial
/// success would leave the admin unable to retry: re-sending the corrected batch would duplicate the
/// 18 that already exist, and re-sending only the 2 means hand-editing a list the UI just built.
/// </summary>
public sealed record BulkCreateQuotasResultDto(
    IReadOnlyList<QuotaSummaryDto> Created,
    IReadOnlyList<BulkQuotaFailureDto> Failures)
{
    public bool IsSuccess => Failures.Count == 0;
    public int CreatedCount => Created.Count;
}
