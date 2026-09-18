using MediatR;
using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Compensation.DTOs;
using Wasnie.Application.Compensation.Mappings;
using Wasnie.Application.Compensation.Queries.Plans;
using Wasnie.Domain.Authorization;
using Wasnie.Domain.Common.Results;
using Wasnie.Domain.Compensation.Enums;

namespace Wasnie.Application.Compensation.Handlers.Plans;

/// <summary>
/// One plan with its rules.
///
/// ★★ TWO WAYS IN, AND THE SECOND ONE IS THE WHOLE OF KAN-93 BUG 6. <c>Plans.Read</c> opens the
/// catalogue and every plan in it — that is administration. <c>Plans.ReadOwn</c> opens ONLY the plans
/// behind the reader's own assignments, decided by <see cref="IPlanAccessGuard"/>, so a rep can check
/// the arithmetic behind their own commission. Before this, clicking their own plan from the
/// Assignments screen ended in Access Denied, and "see why you were paid that" was a promise the
/// product could not keep.
///
/// ★★ THIS IS THE ONLY PLAN ENDPOINT THAT OPENS. ListPlans, the version history, the simulator, the
/// trigger fields, the category values and the multi-plan payee lookup all still require
/// <c>Plans.Read</c> and are untouched — a rep must reach their own plan, never the catalogue.
///
/// ★★ A REFUSAL STILL LEAVES A ROW (§B1). The guard alone would have thrown silently, and a denial
/// nobody can see afterwards is the failure mode this codebase has a rule about — it also cost an hour
/// of debugging the first time, because the 403 the user reported appeared nowhere in the log. So the
/// refusal path goes through <c>RequireAsync</c>, which writes the PermissionDenied audit entry and
/// then throws exactly as it does everywhere else. It cannot wrongly admit anybody: a Plans.Read
/// holder never reaches it, because the guard already returned true for them.
/// </summary>
public sealed class GetPlanByIdHandler(
    IApplicationDbContext db,
    IAuthorizationService authorizationService,
    IPlanAccessGuard planAccessGuard)
    : IRequestHandler<GetPlanByIdQuery, Result<PlanDto>>
{
    public async Task<Result<PlanDto>> Handle(GetPlanByIdQuery request, CancellationToken cancellationToken)
    {
        // ★ THE GUARD IS ASKED FIRST AND IT ANSWERS BOTH QUESTIONS. It returns true for anyone holding
        // Plans.Read, so the administrator path is unchanged; for a Plans.ReadOwn holder it resolves
        // the plan against their own assignments. Asking RequireAsync FIRST would refuse the very
        // reader this ticket exists to admit.
        if (!await planAccessGuard.CanReadAsync(request.PlanId, cancellationToken))
            await authorizationService.RequireAsync(Permission.PlansRead, cancellationToken);
        // Return active rules AND STOPPED ONES. Deleting a rule soft-deactivates it (IsActive=false),
        // and returning those makes them reappear in the UI where editing them fails with "rule not
        // found in this plan" — that is why the filter exists. But a STOPPED rule is not a deleted
        // one: hiding it would make the plan look like it never had that rule, which is exactly the
        // silence the brake was built to end. The reader has to see that a rule was braked, when,
        // and why, so `StoppedAt != null` is pulled back in and Plan.UpdateRule accepts it.
        // Order by SortOrder server-side; ThenBy(Id) is a stable tie-break for colliding orders.
        var plan = await db.CompensationPlans
            .Include(p => p.Rules.Where(r => r.IsActive || r.StoppedAt != null).OrderBy(r => r.SortOrder).ThenBy(r => r.Id))
            .FirstOrDefaultAsync(p => p.Id == request.PlanId, cancellationToken);

        if (plan is null)
        {
            return Result<PlanDto>.Failure("Plan not found.");
        }

        // Counted here rather than derived from a navigation: PlanAssignment is a separate aggregate.
        // The archive confirmation shows this number, so it has to be the same predicate
        // ArchivePlanHandler deactivates on — Active assignments of this plan, nothing else.
        var activeAssignmentCount = await db.PlanAssignments
            .CountAsync(a => a.PlanId == plan.Id && a.Status == AssignmentStatus.Active, cancellationToken);

        return Result<PlanDto>.Success(CompensationMapper.ToPlanDto(plan, activeAssignmentCount));
    }
}
