namespace Wasnie.Application.Compensation.DTOs;

/// <param name="From">The range actually applied, echoed back so the screen labels what it shows.</param>
/// <param name="CommissionsBand">
/// Total / Paid / Unpaid for THIS payee over the range.
///
/// ★ THE SAME RECORD THE DASHBOARD USES, not a payee-shaped copy. The two answer the same question at
/// different scopes; a parallel type would let the two drift apart in wording, in rounding, or in what
/// "paid" means, and the whole value of these three figures is that they agree everywhere.
/// </param>
public sealed record PayeeDashboardDto(
    DateOnly From,
    DateOnly To,
    DashboardCommissionsBandDto CommissionsBand,
    IReadOnlyList<QuotaAttainmentDto> AttainmentItems,
    IReadOnlyList<SalesTrendPointDto> SalesTrend,
    IReadOnlyList<QuotaSummaryDto> RecentQuotas,
    IReadOnlyList<PlanAssignmentSummaryDto> RecentAssignments);

public sealed record SalesTrendPointDto(
    int Year,
    int Month,
    string MonthLabel,
    decimal Amount,
    string Currency);
