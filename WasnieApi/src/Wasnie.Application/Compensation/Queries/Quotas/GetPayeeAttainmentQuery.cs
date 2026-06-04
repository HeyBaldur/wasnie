using MediatR;
using Wasnie.Application.Compensation.DTOs;
using Wasnie.Domain.Common.Results;

namespace Wasnie.Application.Compensation.Queries.Quotas;

public sealed record GetPayeeAttainmentQuery(Guid PayeeId) : IRequest<Result<IReadOnlyList<QuotaAttainmentDto>>>;
