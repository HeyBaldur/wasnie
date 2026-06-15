using MediatR;
using Wasnie.Domain.Common.Results;

namespace Wasnie.Application.Features.Subscription.Commands;

public sealed record SelectFreePlanCommand : IRequest<Result<bool>>;
