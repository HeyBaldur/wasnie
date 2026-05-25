using MediatR;
using Wasnie.Application.Compensation.DTOs;
using Wasnie.Domain.Common.Results;

namespace Wasnie.Application.Compensation.Queries.Quotas;

public sealed record ListQuotasByPayeeQuery(Guid PayeeId) : IRequest<Result<IList<QuotaDto>>>;
