using MediatR;
using Wasnie.Domain.Common.Results;

namespace Wasnie.Application.Features.Subscription.Commands;

public sealed record RevertSubscriptionCancellationCommand : IRequest<Result<bool>>;
