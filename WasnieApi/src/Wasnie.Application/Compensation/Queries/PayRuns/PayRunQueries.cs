using MediatR;
using Wasnie.Application.Common.Models;
using Wasnie.Application.Compensation.DTOs;
using Wasnie.Domain.Common.Results;

namespace Wasnie.Application.Compensation.Queries.PayRuns;

public sealed record PayRunFilterQuery
{
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 25;
    public string SortOrder { get; init; } = "desc";

    public string? Status { get; init; }          // Draft|Approved|Paid
    public DateOnly? PeriodFrom { get; init; }    // runs where PeriodStart >= this
    public DateOnly? PeriodTo { get; init; }      // runs where PeriodEnd <= this
}

public sealed record ListPayRunsQuery(PayRunFilterQuery Filter)
    : IRequest<Result<PagedResult<PayRunListItemDto>>>;

public sealed record PayRunPayoutsFilter
{
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 25;
    public bool ExcludeZero { get; init; } = false;
}

public sealed record GetPayRunByIdQuery(Guid Id, PayRunPayoutsFilter PayoutsFilter)
    : IRequest<Result<PayRunDetailDto>>;
