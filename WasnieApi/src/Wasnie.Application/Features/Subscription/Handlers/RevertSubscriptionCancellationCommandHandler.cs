using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Wasnie.Application.Common.Abstractions;
using Wasnie.Application.Common.DTOs;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Features.Subscription.Commands;
using Wasnie.Domain.Audit;
using Wasnie.Domain.Common.Results;

namespace Wasnie.Application.Features.Subscription.Handlers;

public sealed class RevertSubscriptionCancellationCommandHandler(
    IApplicationDbContext db,
    ITenantContext tenantContext,
    ICurrentUserService currentUser,
    IStripeSubscriptionManagementService stripeManagement,
    IAuditService auditService,
    IClock clock,
    ILogger<RevertSubscriptionCancellationCommandHandler> logger)
    : IRequestHandler<RevertSubscriptionCancellationCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(
        RevertSubscriptionCancellationCommand request,
        CancellationToken cancellationToken)
    {
        var subscription = await db.UserSubscriptions
            .FirstOrDefaultAsync(s => s.TenantId == tenantContext.TenantId, cancellationToken);

        if (subscription is null)
            return Result<bool>.Failure("No subscription found.");

        if (string.IsNullOrEmpty(subscription.StripeSubscriptionId))
            return Result<bool>.Failure("No Stripe subscription associated with this account.");

        if (!subscription.CancelAtPeriodEnd)
            return Result<bool>.Success(true); // idempotent

        try
        {
            await stripeManagement.RevertCancellationAsync(
                subscription.StripeSubscriptionId,
                cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Failed to revert Stripe cancel_at_period_end for subscription {SubscriptionId}",
                subscription.StripeSubscriptionId);
            return Result<bool>.Failure("Failed to revert cancellation. Please try again.");
        }

        subscription.ClearCancellationSchedule(clock.UtcNowOffset);
        await db.SaveChangesAsync(cancellationToken);

        await auditService.LogAsync(new AuditEntry(
            TenantId: tenantContext.TenantId,
            Action: AuditActions.SubscriptionCancelReverted,
            ResourceType: ResourceTypes.Subscription,
            ResourceId: subscription.StripeSubscriptionId,
            ActorUserId: currentUser.UserId ?? "unknown",
            ActorEmail: currentUser.Email ?? string.Empty,
            DisplayName: $"Subscription cancellation reverted by user: {subscription.StripeSubscriptionId}"),
            cancellationToken);

        logger.LogInformation(
            "Tenant {TenantId} subscription cancellation reverted by user",
            tenantContext.TenantId);

        return Result<bool>.Success(true);
    }
}
