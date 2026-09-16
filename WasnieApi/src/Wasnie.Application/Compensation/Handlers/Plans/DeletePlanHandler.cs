using MediatR;
using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Compensation.Commands.Plans;
using Wasnie.Application.Compensation.Common;
using Wasnie.Domain.Authorization;
using Wasnie.Domain.Common.Results;
using Wasnie.Domain.Compensation.Plans;
using Wasnie.Domain.Exceptions;

namespace Wasnie.Application.Compensation.Handlers.Plans;

/// <summary>
/// Physically deletes a Draft plan and its rules — only when nothing points at it (KAN-69).
///
/// ⛔ THE MONEY GATE IS <see cref="PlanDeletionBlockers"/>. Only the rules have a foreign key to the plan;
/// everything else (assignments, quotas, credits, payouts, ledger, closures) would be orphaned silently.
/// Any one of them refuses the whole delete with a code, and nothing is removed.
/// </summary>
public sealed class DeletePlanHandler(IApplicationDbContext db, IAuthorizationService authorizationService)
    : IRequestHandler<DeletePlanCommand, Result>
{
    public async Task<Result> Handle(DeletePlanCommand request, CancellationToken cancellationToken)
    {
        await authorizationService.RequireAsync(Permission.PlansDelete, cancellationToken);
        var plan = await db.CompensationPlans
            .Include(p => p.Rules)
            .FirstOrDefaultAsync(p => p.Id == request.PlanId, cancellationToken);

        if (plan is null)
            return Result.Failure("Plan not found.");

        try
        {
            plan.CheckDeletable();

            var blockers = await PlanDeletionBlockers.FindAsync(db, [plan.Id], cancellationToken);
            if (blockers.TryGetValue(plan.Id, out var found))
                throw new DomainCodedException(PlanDeleteInvariant.HasDependencies, new Dictionary<string, object?>
                {
                    ["blockers"] = found.Select(b => b.ToString()).Order().ToArray(),
                });

            request.Deleted = new DeletedPlanFacts(
                plan.Name, plan.Version, plan.Currency,
                plan.EffectivePeriod.Start, plan.EffectivePeriod.End, plan.Rules.Count);

            // Rules go with the plan: they are loaded, so EF removes them in this same SaveChanges (and the
            // PlanRules foreign key cascades in the database as well). None of them generated anything —
            // a credit or payout would have been a blocker above.
            db.CompensationPlans.Remove(plan);
            await db.SaveChangesAsync(cancellationToken);
            return Result.Success();
        }
        // ★ BEFORE the general catch: DomainCodedException derives from DomainException, and folding it
        // into Result.Failure(ex.Message) would send the bare code as an English "message" and lose the
        // translation contract. It propagates to the middleware as 422 { code, parameters }.
        catch (DomainCodedException)
        {
            throw;
        }
        catch (DomainException ex)
        {
            return Result.Failure(ex.Message);
        }
    }
}
