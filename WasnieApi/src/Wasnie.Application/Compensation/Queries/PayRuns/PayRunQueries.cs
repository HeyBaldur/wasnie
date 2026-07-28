using MediatR;
using Wasnie.Application.Common.Models;
using Wasnie.Application.Compensation.DTOs;
using Wasnie.Application.Compensation.Queries.Transactions;
using Wasnie.Domain.Common.Results;

namespace Wasnie.Application.Compensation.Queries.PayRuns;

public sealed record PayRunFilterQuery
{
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 25;
    public string SortOrder { get; init; } = "desc";

    public string? Status { get; init; }          // Draft|Approved|Paid
    // COMPENSATION period, not the creation timestamp: a run matches when its own period
    // INTERSECTS [PeriodFrom, PeriodTo]. Same semantics as ListPayoutsQuery and the rest of the domain.
    public DateOnly? PeriodFrom { get; init; }    // runs whose PeriodEnd >= this
    public DateOnly? PeriodTo { get; init; }      // runs whose PeriodStart <= this
}

public sealed record ListPayRunsQuery(PayRunFilterQuery Filter)
    : IRequest<Result<PagedResult<PayRunListItemDto>>>;

public sealed record PayRunPayoutsFilter
{
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 25;
    public bool ExcludeZero { get; init; } = false;

    // Rich filter fields — mirrors PayoutFilterQuery (PayRunId is injected by the handler).
    public string? PayeeIds { get; init; }
    public string? PlanIds { get; init; }
    public string? Status { get; init; }
    public string? Currencies { get; init; }
    public DateOnly? PeriodFrom { get; init; }
    public DateOnly? PeriodTo { get; init; }
    public decimal? AmountMin { get; init; }
    public decimal? AmountMax { get; init; }
}

public sealed record GetPayRunByIdQuery(Guid Id, PayRunPayoutsFilter PayoutsFilter)
    : IRequest<Result<PayRunDetailDto>>;

public sealed record ExportPayRunsQuery(PayRunFilterQuery Filter)
    : IRequest<Result<ExportResult>>;

public sealed record GetPayRunOverlapsQuery(Guid PayRunId)
    : IRequest<Result<IReadOnlyList<OverlappingPayRunDto>>>;
