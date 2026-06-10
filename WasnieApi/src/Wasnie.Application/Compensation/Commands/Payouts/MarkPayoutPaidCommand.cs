using MediatR;
using Wasnie.Domain.Common.Results;

namespace Wasnie.Application.Compensation.Commands.Payouts;

public sealed record MarkPayoutPaidCommand(Guid PayoutId) : IRequest<Result>;
