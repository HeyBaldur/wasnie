using MediatR;
using Wasnie.Domain.Common.Results;

namespace Wasnie.Application.Compensation.Commands.Quotas;

public sealed record CloseQuotaCommand(Guid QuotaId) : IRequest<Result>;
