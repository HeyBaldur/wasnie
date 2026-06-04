using MediatR;
using Wasnie.Application.Common.Models;
using Wasnie.Application.Compensation.DTOs;
using Wasnie.Domain.Common.Results;

namespace Wasnie.Application.Compensation.Queries.Credits;

public sealed record CreditFilterQuery
{
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 25;
    public string? SortBy { get; init; }
    public string SortOrder { get; init; } = "desc";

    public string? PayeeIds { get; init; }       // comma-separated GUIDs
    public string? PlanIds { get; init; }         // comma-separated GUIDs
    public string? Status { get; init; }          // "Active"|"Superseded"|"All" — default Active
    public DateOnly? AllocatedFrom { get; init; }
    public DateOnly? AllocatedTo { get; init; }
    public decimal? AmountMin { get; init; }
    public decimal? AmountMax { get; init; }
    public string? Currencies { get; init; }      // comma-separated currency codes
    public string? Reference { get; init; }       // substring on ReferenceNumber
}

public sealed record ListCreditsQuery(CreditFilterQuery Filter)
    : IRequest<Result<PagedResult<CreditListDto>>>;

public sealed record GetCreditCountersQuery(CreditFilterQuery Filter)
    : IRequest<Result<CreditCountersDto>>;

public sealed record GetCreditsByPayeeQuery(CreditFilterQuery Filter)
    : IRequest<Result<IReadOnlyList<CreditByPayeeDto>>>;

public sealed record GetCreditByIdQuery(Guid Id)
    : IRequest<Result<CreditDetailDto>>;
