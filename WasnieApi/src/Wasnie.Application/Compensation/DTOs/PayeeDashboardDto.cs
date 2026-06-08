namespace Wasnie.Application.Compensation.DTOs;

public sealed record PayeeDashboardDto(
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
