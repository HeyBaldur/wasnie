namespace Wasnie.Application.Compensation.DTOs;

/// <summary>
/// What deactivating a set of assignments would strand.
/// </summary>
/// <param name="StrandedByCurrency">
/// The unpaid commission that would stop being reachable by any pay run. EMPTY IS THE NORMAL CASE and
/// must stay cheap to render: most deactivations strand nothing, and a dialog that cries wolf on every
/// one of them stops being read.
/// </param>
/// <param name="Items">One per assignment that would strand something. Assignments that strand nothing are omitted.</param>
public sealed record DeactivationImpactDto(
    IReadOnlyList<CurrencyTotalDto> StrandedByCurrency,
    IReadOnlyList<DeactivationImpactItemDto> Items);

/// <param name="PayeeName">Named because the bulk dialog lists several, and "3 payees" is not actionable.</param>
public sealed record DeactivationImpactItemDto(
    Guid AssignmentId,
    Guid PayeeId,
    string PayeeName,
    Guid PlanId,
    string PlanName,
    int CreditCount,
    decimal Amount,
    string Currency);
