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
    // COMPENSATION period, not the calculation timestamp: a payout matches when its own period
    // INTERSECTS [PeriodFrom, PeriodTo]. Same semantics as everywhere else in the domain.
    public DateOnly? PeriodFrom { get; init; } // payouts whose Period.End >= this
    public DateOnly? PeriodTo { get; init; }   // payouts whose Period.Start <= this

    // PAYMENT date (cash flow), a different question from the compensation period above and therefore a
    // separate pair of parameters rather than a mode switch on PeriodFrom/PeriodTo. "Which payouts does
    // this month's payroll cover?" (period, intersection) and "what money left the account this month?"
    // (payment date, containment) are both legitimate and both in use — payroll export needs the first,
    // the dashboard cash-flow card needs the second. Collapsing them would silently change the export.
    //
    // Matches on PaidAt, so only Paid payouts can ever satisfy it (PaidAt is null in every other status).
    public DateOnly? PaidFrom { get; init; }   // payouts whose PaidAt >= this day, 00:00:00 UTC
    public DateOnly? PaidTo { get; init; }     // payouts whose PaidAt <= this day, 23:59:59.999… UTC
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

/// <summary>
/// Given a payout ID, returns all OTHER payouts of the same payee with an overlapping period
/// that are in Approved or Paid status.
/// </summary>
public sealed record GetPayoutOverlapsQuery(Guid PayoutId)
    : IRequest<Result<IReadOnlyList<OverlappingPayoutDto>>>;

/// <summary>
/// Given a list of payout IDs, returns how many of them have at least one overlapping
/// Approved/Paid payout for the same payee.
/// </summary>
public sealed record CheckPayoutsOverlapsQuery(IReadOnlyList<Guid> PayoutIds)
    : IRequest<Result<int>>;
