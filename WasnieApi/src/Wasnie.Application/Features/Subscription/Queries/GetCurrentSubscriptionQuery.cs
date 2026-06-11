using MediatR;
using Wasnie.Application.Features.Subscription.DTOs;
using Wasnie.Domain.Common.Results;

namespace Wasnie.Application.Features.Subscription.Queries;

public sealed record GetCurrentSubscriptionQuery : IRequest<Result<UserSubscriptionDto>>;
