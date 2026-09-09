using MediatR;
using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Authorization;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Compensation.DTOs;
using Wasnie.Application.Compensation.Queries.Assignments;
using Wasnie.Domain.Authorization;
using Wasnie.Domain.Common.Results;
using Wasnie.Domain.Compensation.Enums;

namespace Wasnie.Application.Compensation.Handlers.Assignments;

/// <summary>
/// Answers, before the click, how much unpaid commission a deactivation would put out of reach.
///
/// ★★ IT MIRRORS THE ENGINE'S GATE. <c>CalculatePayoutsForPeriodHandler</c> only ever walks assignments
/// that are <c>Active</c> on a non-archived plan. So the money at risk is the payee's unpaid commission
/// on that plan — and it is only AT RISK if this assignment is the last Active link between the two.
///
/// ★ THE "LAST ACTIVE LINK" TEST IS THE WHOLE SUBTLETY. A payee can hold more than one assignment to
/// the same plan (a renewal overlapping the one it replaces is the ordinary case). Switching one off
/// while another stays Active strands nothing, and warning there would train the reader to click
/// through the dialog that one day is telling the truth.
///
/// ★ IT ANSWERS FOR THE WHOLE BATCH AT ONCE, not per assignment in a loop: deactivating two assignments
/// to the same plan strands the money once, and two dialogs each claiming the full amount would double
/// count it.
/// </summary>
public sealed class GetDeactivationImpactHandler(
    IApplicationDbContext db,
    IAuthorizationService authorizationService)
    : IRequestHandler<GetDeactivationImpactQuery, Result<DeactivationImpactDto>>
{
    public async Task<Result<DeactivationImpactDto>> Handle(
        GetDeactivationImpactQuery request, CancellationToken cancellationToken)
    {
        // The reader of this is the person about to deactivate. Gating it on a credits permission would
        // hide the warning from exactly the role that needs it.
        await authorizationService.RequireAsync(Permission.AssignmentsUpdate, cancellationToken);

        var ids = request.AssignmentIds?.Distinct().ToList() ?? [];
        if (ids.Count == 0)
            return Result<DeactivationImpactDto>.Success(new DeactivationImpactDto([], []));

        var targets = await db.PlanAssignments
            .Where(a => ids.Contains(a.Id))
            .Select(a => new { a.Id, a.PayeeId, a.PlanId, a.Status })
            .ToListAsync(cancellationToken);

        // Only Active ones can strand anything: deactivating what is already deactivated changes nothing.
        var active = targets.Where(a => a.Status == AssignmentStatus.Active).ToList();
        if (active.Count == 0)
            return Result<DeactivationImpactDto>.Success(new DeactivationImpactDto([], []));

        var payeeIds = active.Select(a => a.PayeeId).Distinct().ToList();
        var planIds = active.Select(a => a.PlanId).Distinct().ToList();

        // Every Active assignment on those pairs, including ones NOT being deactivated — those are the
        // survivors that keep the money reachable.
        var survivors = await db.PlanAssignments
            .Where(a => a.Status == AssignmentStatus.Active
                     && payeeIds.Contains(a.PayeeId)
                     && planIds.Contains(a.PlanId)
                     && !ids.Contains(a.Id))
            .Select(a => new { a.PayeeId, a.PlanId })
            .ToListAsync(cancellationToken);

        var stillReachable = survivors.Select(s => (s.PayeeId, s.PlanId)).ToHashSet();

        // One row per payee+plan pair that would lose its last Active link. Keyed so two assignments to
        // the same plan in one batch collapse into a single warning.
        var atRisk = active
            .Where(a => !stillReachable.Contains((a.PayeeId, a.PlanId)))
            .GroupBy(a => (a.PayeeId, a.PlanId))
            .ToDictionary(g => g.Key, g => g.First().Id);

        if (atRisk.Count == 0)
            return Result<DeactivationImpactDto>.Success(new DeactivationImpactDto([], []));

        var riskPayeeIds = atRisk.Keys.Select(k => k.PayeeId).Distinct().ToList();
        var riskPlanIds = atRisk.Keys.Select(k => k.PlanId).Distinct().ToList();

        // Unpaid = not paid, not closed, not superseded. No date window on purpose: the debt is spread
        // across periods, and a window is precisely how it stayed invisible.
        var owed = await db.Credits
            .Where(c => riskPayeeIds.Contains(c.PayeeId)
                     && riskPlanIds.Contains(c.PlanId)
                     && c.SupersededAt == null
                     && c.ConsumedAt == null
                     && c.ClosedAt == null)
            .Select(c => new
            {
                c.PayeeId,
                c.PlanId,
                c.CreditedAmount.Amount,
                c.CreditedAmount.Currency,
            })
            .ToListAsync(cancellationToken);

        if (owed.Count == 0)
            return Result<DeactivationImpactDto>.Success(new DeactivationImpactDto([], []));

        var payeeNames = await db.Payees
            .Where(p => riskPayeeIds.Contains(p.Id))
            .Select(p => new { p.Id, p.FullName })
            .ToDictionaryAsync(p => p.Id, p => p.FullName, cancellationToken);

        var planNames = await db.CompensationPlans
            .Where(p => riskPlanIds.Contains(p.Id))
            .Select(p => new { p.Id, p.Name })
            .ToDictionaryAsync(p => p.Id, p => p.Name, cancellationToken);

        var items = new List<DeactivationImpactItemDto>();

        foreach (var group in owed.GroupBy(c => new { c.PayeeId, c.PlanId, c.Currency }))
        {
            var key = (group.Key.PayeeId, group.Key.PlanId);
            if (!atRisk.TryGetValue(key, out var assignmentId)) continue;

            items.Add(new DeactivationImpactItemDto(
                AssignmentId: assignmentId,
                PayeeId: group.Key.PayeeId,
                PayeeName: payeeNames.TryGetValue(group.Key.PayeeId, out var pn) ? pn : string.Empty,
                PlanId: group.Key.PlanId,
                PlanName: planNames.TryGetValue(group.Key.PlanId, out var pl) ? pl : string.Empty,
                CreditCount: group.Count(),
                Amount: group.Sum(c => c.Amount),
                Currency: group.Key.Currency));
        }

        var totals = items
            .GroupBy(i => i.Currency)
            .Select(g => new CurrencyTotalDto(g.Sum(i => i.Amount), g.Key))
            .OrderBy(t => t.Currency)
            .ToList();

        return Result<DeactivationImpactDto>.Success(
            new DeactivationImpactDto(totals, items.OrderByDescending(i => i.Amount).ToList()));
    }
}
