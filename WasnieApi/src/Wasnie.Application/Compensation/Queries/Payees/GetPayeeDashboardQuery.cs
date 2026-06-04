using MediatR;
using Wasnie.Application.Compensation.DTOs;
using Wasnie.Domain.Common.Results;

namespace Wasnie.Application.Compensation.Queries.Payees;

public sealed record GetPayeeDashboardQuery(Guid PayeeId, string Period = "active") : IRequest<Result<PayeeDashboardDto>>;
