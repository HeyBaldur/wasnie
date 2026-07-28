using MediatR;
using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Common.Abstractions;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Compensation.Commands.Plans;
using Wasnie.Application.Common.DTOs;
using Wasnie.Domain.Audit;
using Wasnie.Domain.Authorization;
using Wasnie.Domain.Common.Results;
using Wasnie.Domain.Exceptions;

namespace Wasnie.Application.Compensation.Handlers.Plans;

/// <summary>
/// Turns the clawback on (or off) for one plan. Setting a maturation window is what ACTIVATES
/// clawbacks for that plan — every plan starts with both fields null and claws nothing back, so
/// this handler is the switch, not a tuning knob.
/// </summary>
public sealed class SetPlanClawbackPolicyHandler(
    IApplicationDbContext db,
    IAuthorizationService authorizationService,
    ICurrentUserService currentUser,
    ITenantContext tenantContext,
    IClock clock,
    IAuditService audit)
    : IRequestHandler<SetPlanClawbackPolicyCommand, Result>
{
    public async Task<Result> Handle(
        SetPlanClawbackPolicyCommand request, CancellationToken cancellationToken)
    {
        await authorizationService.RequireAsync(Permission.PlansUpdate, cancellationToken);

        var plan = await db.CompensationPlans
            .FirstOrDefaultAsync(p => p.Id == request.PlanId, cancellationToken);

        if (plan is null)
            return Result.Failure("Plan not found.");

        var before = (plan.ClawbackMaturationDays, plan.ClawbackCapPercent);

        try
        {
            plan.SetClawbackPolicy(
                request.MaturationDays, request.CapPercent,
                currentUser.Email ?? currentUser.UserId ?? "system", clock.UtcNowOffset);
        }
        catch (DomainException ex)
        {
            return Result.Failure(ex.Message);
        }

        await db.SaveChangesAsync(cancellationToken);

        var actor = currentUser.Email ?? currentUser.UserId ?? "system";
        await audit.LogAsync(new AuditEntry(
            TenantId: tenantContext.TenantId,
            Action: AuditActions.PlanClawbackPolicyChanged,
            ResourceType: ResourceTypes.Plan,
            ResourceId: plan.Id.ToString(),
            ActorUserId: currentUser.UserId ?? actor,
            ActorEmail: actor,
            DisplayName: plan.Name,
            Metadata: new Dictionary<string, string>
            {
                ["maturationDaysBefore"] = before.ClawbackMaturationDays?.ToString() ?? "none",
                ["maturationDaysAfter"] = plan.ClawbackMaturationDays?.ToString() ?? "none",
                ["capPercentBefore"] = before.ClawbackCapPercent?.ToString() ?? "none",
                ["capPercentAfter"] = plan.ClawbackCapPercent?.ToString() ?? "none",
            }), cancellationToken);

        return Result.Success();
    }
}
