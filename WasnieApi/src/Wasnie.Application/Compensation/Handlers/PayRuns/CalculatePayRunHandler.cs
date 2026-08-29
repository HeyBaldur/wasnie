using MediatR;
using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Common.Abstractions;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Compensation.Commands.PayRuns;
using Wasnie.Application.Compensation.Commands.Payouts;
using Wasnie.Domain.Authorization;
using Wasnie.Domain.Common.Results;
using Wasnie.Domain.Compensation.Enums;
using Wasnie.Domain.Compensation.Payouts;

namespace Wasnie.Application.Compensation.Handlers.PayRuns;

public sealed class CalculatePayRunHandler(
    IApplicationDbContext db,
    IAuthorizationService authorizationService,
    ITenantContext tenantContext,
    ICurrentUserService currentUser,
    IClock clock,
    IGuidGenerator guid,
    ISender sender)
    : IRequestHandler<CalculatePayRunCommand, Result<CalculatePayRunResult>>
{
    public async Task<Result<CalculatePayRunResult>> Handle(
        CalculatePayRunCommand request, CancellationToken cancellationToken)
    {
        await authorizationService.RequireAsync(Permission.PayoutsCalculate, cancellationToken);

        if (request.PeriodStart > request.PeriodEnd)
            return Result<CalculatePayRunResult>.Failure("PeriodStart must be on or before PeriodEnd.");

        var tenantId = tenantContext.TenantId;
        var actor = currentUser.Email ?? currentUser.UserId ?? "system";
        var now = clock.UtcNowOffset;

        // ── 1. Determine which PayRun to use ─────────────────────────────────
        // HasQueryFilter already scopes to tenantId.
        // Load all runs for this period so we can:
        //   a) reuse an existing Draft, or
        //   b) create a supplemental run after the period has one or more Paid/Approved runs.
        var existingRuns = await db.PayRuns
            .Where(r => r.PeriodStart == request.PeriodStart && r.PeriodEnd == request.PeriodEnd)
            .ToListAsync(cancellationToken);

        PayRun payRun;
        var isSupplemental = false;

        var draftRun = existingRuns.FirstOrDefault(r => r.Status == PayRunStatus.Draft);

        if (draftRun is not null)
        {
            // A Draft already exists — recalculate it (idempotent path, same as before).
            payRun = draftRun;
        }
        else if (existingRuns.Count == 0)
        {
            // No run for this period at all — create the primary run (sequence = 0).
            payRun = PayRun.Open(
                tenantId: tenantId,
                periodStart: request.PeriodStart,
                periodEnd: request.PeriodEnd,
                createdBy: actor,
                id: guid.NewGuid(),
                now: now,
                supplementalSequence: 0);

            db.PayRuns.Add(payRun);
            await db.SaveChangesAsync(cancellationToken);
        }
        else
        {
            // All existing runs for this period are Approved or Paid — create a supplemental.
            // The supplemental sequence is max(existing) + 1, keeping the unique index constraint.
            var nextSeq = existingRuns.Max(r => r.SupplementalSequence) + 1;

            payRun = PayRun.Open(
                tenantId: tenantId,
                periodStart: request.PeriodStart,
                periodEnd: request.PeriodEnd,
                createdBy: actor,
                id: guid.NewGuid(),
                now: now,
                supplementalSequence: nextSeq);

            db.PayRuns.Add(payRun);
            await db.SaveChangesAsync(cancellationToken);
            isSupplemental = true;
        }

        // ── 2. Run per-payout calculation (existing idempotent engine) ────────
        // The engine's own anti-double-pay guards (Layers 1–3) prevent re-paying
        // already-Approved or already-Paid payees, so supplemental runs only
        // pick up payees/plans that were not present in prior runs.
        var calcResult = await sender.Send(
            new CalculatePayoutsForPeriodCommand(request.PeriodStart, request.PeriodEnd),
            cancellationToken);

        if (!calcResult.IsSuccess)
            return Result<CalculatePayRunResult>.Failure(calcResult.Error!);

        // ── 3. Assign PayRunId to payouts that belong to this run ────────────
        // A payout belongs to THIS run if it either has no run assigned yet, or was
        // previously assigned to this run (re-calculation path).
        // DateOnly owned-type EF caveat: period equality filtered in-memory.
        var candidatePayouts = await db.CompensationPayouts
            .IgnoreQueryFilters()
            .Where(p => p.TenantId == tenantId
                     && (p.PayRunId == null || p.PayRunId == payRun.Id))
            .ToListAsync(cancellationToken);

        var runPayouts = candidatePayouts
            .Where(p => p.Period.Start == request.PeriodStart
                     && p.Period.End == request.PeriodEnd
                     && p.Status != CompensationPayoutStatus.Disputed)
            .ToList();

        foreach (var payout in runPayouts)
            payout.AssignToRun(payRun.Id);

        // ── 4. Recompute roll-ups and persist ─────────────────────────────────
        payRun.UpdateRollUps(runPayouts);
        await db.SaveChangesAsync(cancellationToken);

        return Result<CalculatePayRunResult>.Success(new CalculatePayRunResult(
            PayRunId: payRun.Id,
            PayoutsCreated: calcResult.Value!.PayoutsCreated,
            Conflicts: calcResult.Value.Conflicts,
            Warnings: calcResult.Value.Warnings,
            // Verbatim. This handler orchestrates the run; it establishes no cause of its own and must
            // never soften or summarise one the engine reported.
            Diagnostics: calcResult.Value.Diagnostics,
            IsSupplemental: isSupplemental,
            SupplementalSequence: payRun.SupplementalSequence));
    }
}
