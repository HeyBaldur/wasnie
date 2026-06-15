using MediatR;
using Wasnie.Application.Features.Subscription.DTOs;
using Wasnie.Domain.Common.Results;

namespace Wasnie.Application.Features.Subscription.Commands;

public sealed record CreateBillingPortalSessionCommand : IRequest<Result<BillingPortalSessionDto>>;
