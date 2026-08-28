namespace Wasnie.Application.Compensation.DTOs;

// List row — joins: Transaction (ref), Payee (name/code), Plan (name from batch lookup)
public sealed record CreditListDto(
    Guid Id,
    Guid TransactionId,
    string ReferenceNumber,
    Guid PayeeId,
    string? PayeeName,
    string? PayeeCode,
    Guid PlanId,
    string PlanName,
    // RuleId lets the UI deep-link to the specific rule (/plans/{planId}/rules/{ruleId}). Already
    // persisted on Credit.RuleId — exposed read-only, no engine/attribution change.
    Guid RuleId,
    string RuleName,
    decimal OriginalAmount,
    string OriginalCurrency,
    decimal CreditedAmount,
    string CreditedCurrency,
    DateTimeOffset AllocatedAt,
    bool IsSuperseded);

// By-Payee aggregate row
public sealed record CreditByPayeeDto(
    Guid PayeeId,
    string? PayeeName,
    string? PayeeCode,
    int CreditCount,
    IReadOnlyList<CurrencyTotalDto> Totals,
    DateTimeOffset LatestAllocatedAt);

public sealed record CurrencyTotalDto(decimal Amount, string Currency);

// Counter cards for the list page
public sealed record CreditCountersDto(
    int ActiveCount,
    int SupersededCount,
    IReadOnlyList<CurrencyTotalDto> ActiveTotals);

// Full detail — all 5 sections
public sealed record CreditDetailDto(
    // Section A — Summary
    Guid Id,
    bool IsSuperseded,
    DateTimeOffset? SupersededAt,
    string? SupersededBy,
    DateTimeOffset AllocatedAt,
    string AllocatedBy,
    decimal OriginalAmount,
    string OriginalCurrency,
    decimal CreditedAmount,
    string CreditedCurrency,
    decimal SplitPercentage,
    string Role,
    // Section B — Source Transaction
    Guid TransactionId,
    string ReferenceNumber,
    DateOnly TransactionDate,
    decimal TransactionAmount,
    string TransactionCurrency,
    int TransactionQuantity,
    string TransactionStatus,
    Guid? TransactionPayeeId,
    string? PayeeName,
    string? PayeeCode,
    // Section C — Plan & Rule
    Guid PlanId,
    string PlanName,
    int PlanVersion,
    string PlanStatus,
    Guid RuleId,
    string RuleName,
    // Section D — Rule Snapshot
    string RateTableType,
    decimal? FlatRate,
    /// <summary>See RateTableDto.MeasurementBase — same fact, same reason, same bug avoided.</summary>
    string MeasurementBase,
    string RuleSnapshotJson,
    bool TriggerAlways,
    string? TriggerSummary);
