using MediatR;
using Wasnie.Application.Common.Models;
using Wasnie.Application.Compensation.DTOs;
using Wasnie.Application.Compensation.Queries.Transactions;
using Wasnie.Domain.Common.Results;

namespace Wasnie.Application.Compensation.Queries.Payouts;

public sealed record PayoutFilterQuery
{
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 25;
    public string? SortBy { get; init; }
    public string SortOrder { get; init; } = "desc";

    public string? PayeeIds { get; init; }     // comma-separated GUIDs
    public string? PlanIds { get; init; }      // comma-separated GUIDs
    public string? Status { get; init; }       // Calculated|Approved|Paid|Disputed|All — default All
    public string? Currencies { get; init; }   // comma-separated 3-letter codes
    public DateOnly? PeriodFrom { get; init; } // payouts where PeriodStart >= this
    public DateOnly? PeriodTo { get; init; }   // payouts where PeriodEnd <= this
    public bool ExcludeZero { get; init; } = false; // exclude payouts with TotalCommission = 0

    // Optional: restrict to a specific pay run (used by the detail export and run-detail sub-table).
    public Guid? PayRunId { get; init; }

    // Optional: filter by payout total commission amount.
    public decimal? AmountMin { get; init; }
    public decimal? AmountMax { get; init; }
}

public sealed record ListPayoutsQuery(PayoutFilterQuery Filter)
    : IRequest<Result<PagedResult<PayoutListItemDto>>>;

public sealed record GetPayoutByIdQuery(Guid Id)
    : IRequest<Result<PayoutDto>>;

public sealed record ExportPayoutPdfQuery(Guid Id)
    : IRequest<Result<ExportResult>>;

public sealed record BulkApprovePayoutsCommand(IReadOnlyList<Guid> PayoutIds)
    : IRequest<Result<BulkApproveResult>>;

public sealed record BulkApproveResult(int Approved, IReadOnlyList<string> Errors);

public sealed record BulkMarkPaidCommand(IReadOnlyList<Guid> PayoutIds)
    : IRequest<Result<BulkMarkPaidResult>>;

public sealed record BulkMarkPaidResult(int Paid, IReadOnlyList<string> Errors);

/// <summary>
/// Returns an .xlsx file containing all Payouts matching the given filter (no pagination).
/// </summary>
public sealed record ExportPayoutsQuery(PayoutFilterQuery Filter)
    : IRequest<Result<ExportResult>>;
