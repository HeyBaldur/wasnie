using MediatR;
using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Authorization;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Compensation.DTOs;
using Wasnie.Application.Compensation.Queries.Payees;
using Wasnie.Domain.Authorization;
using Wasnie.Domain.Common.Results;
using Wasnie.Domain.Compensation.Enums;
using Wasnie.Domain.Compensation.Plans;

namespace Wasnie.Application.Compensation.Handlers.Payees;

/// <summary>
/// Lists the commission this payee is owed that no pay run can reach, and what would unstick each part.
///
/// ★★ THE GATE IT MIRRORS. <c>CalculatePayoutsForPeriodHandler</c> starts from assignments that are
/// <c>Active</c> and whose plan is not archived; anything else is never considered, however long it
/// waits. This screen answers the same question from the other side, so if that gate ever changes this
/// must change with it — the two disagreeing is worse than neither existing, because the screen would
/// promise money the engine will not pay.
/// </summary>
public sealed class GetPayeeUnreachableCommissionHandler(
    IApplicationDbContext db,
    IAuthorizationService authorizationService,
    IPayeeAccessGuard payeeAccessGuard)
    : IRequestHandler<GetPayeeUnreachableCommissionQuery, Result<PayeeUnreachableCommissionDto>>
{
    public async Task<Result<PayeeUnreachableCommissionDto>> Handle(
        GetPayeeUnreachableCommissionQuery request, CancellationToken cancellationToken)
    {
        await authorizationService.RequireAsync(Permission.CreditsRead, cancellationToken);

        // Same refusal shape as the other payee endpoints: an id that is not yours must be
        // indistinguishable from one that does not exist, or the 404 becomes an enumeration oracle.
        if (!await payeeAccessGuard.CanReadAsync(request.PayeeId, cancellationToken))
            return Result<PayeeUnreachableCommissionDto>.Failure(PayeeAccessDenied.Message);

        var payeeId = request.PayeeId;

        // Everything still owed: not replaced, not paid, not closed. No date filter — see the query's
        // own note: a window is precisely how this money stayed invisible.
        var owed = await db.Credits
            .Where(c => c.PayeeId == payeeId
                     && c.SupersededAt == null
                     && c.ConsumedAt == null
                     && c.ClosedAt == null)
            .Select(c => new
            {
                c.Id,
                c.PlanId,
                c.TransactionId,
                c.CreditedAmount.Amount,
                c.CreditedAmount.Currency,
            })
            .ToListAsync(cancellationToken);

        if (owed.Count == 0)
        {
            return Result<PayeeUnreachableCommissionDto>.Success(
                new PayeeUnreachableCommissionDto([], []));
        }

        var planIds = owed.Select(c => c.PlanId).Distinct().ToList();

        var plans = await db.CompensationPlans
            .Where(p => planIds.Contains(p.Id))
            .Select(p => new { p.Id, p.Name, p.Status })
            .ToListAsync(cancellationToken);
        var planById = plans.ToDictionary(p => p.Id);

        // Every assignment of this payee to those plans, whatever its status: the deactivated ones are
        // the answer the reader needs, so they must not be filtered out here.
        var assignments = await db.PlanAssignments
            .Where(a => a.PayeeId == payeeId && planIds.Contains(a.PlanId))
            .Select(a => new
            {
                a.Id,
                a.PlanId,
                a.Status,
                Start = a.EffectivePeriod!.Start,
                End = a.EffectivePeriod.End,
            })
            .ToListAsync(cancellationToken);

        // The transaction dates behind the credits — what tells the reader WHICH pay run period to run
        // once the assignment is alive again.
        var txIds = owed.Select(c => c.TransactionId).Distinct().ToList();
        var txDates = await db.CompensationTransactions
            .Where(t => txIds.Contains(t.Id))
            .Select(t => new { t.Id, t.TransactionDate })
            .ToListAsync(cancellationToken);
        var txDateById = txDates.ToDictionary(t => t.Id, t => t.TransactionDate);

        var groups = new List<UnreachableCommissionGroupDto>();

        foreach (var group in owed.GroupBy(c => new { c.PlanId, c.Currency }))
        {
            var planId = group.Key.PlanId;
            planById.TryGetValue(planId, out var plan);

            var forPlan = assignments.Where(a => a.PlanId == planId).ToList();
            var active = forPlan.FirstOrDefault(a => a.Status == AssignmentStatus.Active);

            var planArchived = plan?.Status == PlanStatus.Archived;

            // An ACTIVE assignment on a live plan means the engine can reach this money — it is not
            // stuck, so it does not belong on this screen at all.
            if (active is not null && !planArchived) continue;

            // The most useful thing to name is the one thing to change. An archived plan blocks
            // regardless of the assignment, so it wins; otherwise a deactivated assignment is a single
            // click to undo, and having none at all is a different, larger job.
            var reason = planArchived
                ? UnreachableReason.PlanArchived
                : forPlan.Count > 0
                    ? UnreachableReason.AssignmentDeactivated
                    : UnreachableReason.NoAssignment;

            // The assignment a reader would act on: the most recent one, which is the one they most
            // likely just deactivated.
            var candidate = forPlan
                .OrderByDescending(a => a.End)
                .FirstOrDefault();

            var dates = group
                .Select(c => txDateById.TryGetValue(c.TransactionId, out var d) ? d : (DateOnly?)null)
                .Where(d => d.HasValue)
                .Select(d => d!.Value)
                .ToList();

            groups.Add(new UnreachableCommissionGroupDto(
                PlanId: planId,
                PlanName: plan?.Name ?? planId.ToString("N")[..8],
                PlanStatus: plan?.Status.ToString() ?? "Unknown",
                CreditCount: group.Count(),
                Amount: group.Sum(c => c.Amount),
                Currency: group.Key.Currency,
                Reason: reason,
                AssignmentId: candidate?.Id,
                EffectiveStart: candidate?.Start,
                EffectiveEnd: candidate?.End,
                CoversTransactionsFrom: dates.Count > 0 ? dates.Min() : null,
                CoversTransactionsTo: dates.Count > 0 ? dates.Max() : null));
        }

        var totals = groups
            .GroupBy(g => g.Currency)
            .Select(g => new CurrencyTotalDto(g.Sum(x => x.Amount), g.Key))
            .OrderBy(t => t.Currency)
            .ToList();

        return Result<PayeeUnreachableCommissionDto>.Success(
            new PayeeUnreachableCommissionDto(
                totals,
                groups.OrderByDescending(g => g.Amount).ToList()));
    }
}
