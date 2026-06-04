namespace Wasnie.Application.Compensation.DTOs;

public sealed record PayeeDashboardDto(
    IReadOnlyList<QuotaAttainmentDto> AttainmentItems,
    IReadOnlyList<EarningsTrendPointDto> EarningsTrend,
    IReadOnlyList<QuotaSummaryDto> RecentQuotas,
    IReadOnlyList<PlanAssignmentSummaryDto> RecentAssignments);

public sealed record EarningsTrendPointDto(
    int Year,
    int Month,
    string MonthLabel,
    decimal Amount,
    string Currency);
