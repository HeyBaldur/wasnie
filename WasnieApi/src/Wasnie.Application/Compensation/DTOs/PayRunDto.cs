using Wasnie.Application.Common.Models;

namespace Wasnie.Application.Compensation.DTOs;

public sealed record PayRunListItemDto(
    Guid Id,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    string Status,
    int PayeeCount,
    int PaidPayeeCount,
    int ZeroPayoutCount,
    IReadOnlyDictionary<string, decimal> TotalAmounts,
    DateTimeOffset CreatedAt,
    string CreatedBy,
    DateTimeOffset? ApprovedAt,
    string? ApprovedBy,
    DateTimeOffset? PaidAt,
    string? PaidBy);

public sealed record PayRunDetailDto(
    Guid Id,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    string Status,
    int PayeeCount,
    int PaidPayeeCount,
    int ZeroPayoutCount,
    IReadOnlyDictionary<string, decimal> TotalAmounts,
    DateTimeOffset CreatedAt,
    string CreatedBy,
    DateTimeOffset? ApprovedAt,
    string? ApprovedBy,
    DateTimeOffset? PaidAt,
    string? PaidBy,
    PagedResult<PayoutListItemDto> Payouts);
