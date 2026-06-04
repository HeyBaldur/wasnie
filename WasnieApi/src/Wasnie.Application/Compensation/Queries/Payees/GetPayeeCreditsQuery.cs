using MediatR;
using Wasnie.Application.Common.Models;
using Wasnie.Application.Compensation.DTOs;
using Wasnie.Domain.Common.Results;

namespace Wasnie.Application.Compensation.Queries.Payees;

public sealed record GetPayeeCreditsQuery(Guid PayeeId, int Page, int PageSize, string Period = "active")
    : IRequest<Result<PagedResult<CreditListDto>>>;
