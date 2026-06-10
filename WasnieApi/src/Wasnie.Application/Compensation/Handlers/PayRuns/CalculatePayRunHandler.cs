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

        // ── 1. Find or create PayRun ──────────────────────────────────────────
        // HasQueryFilter already scopes to tenantId, so no explicit TenantId == filter needed.
        var payRun = await db.PayRuns
            .FirstOrDefaultAsync(
                r => r.PeriodStart == request.PeriodStart && r.PeriodEnd == request.PeriodEnd,
                cancellationToken);

        if (payRun is not null)
        {
            if (payRun.Status == PayRunStatus.Approved)
                return Result<CalculatePayRunResult>.Failure(
                    "This pay run is Approved. Reopen it before recalculating.");
            if (payRun.Status == PayRunStatus.Paid)
                return Result<CalculatePayRunResult>.Failure(
                    "This pay run is Paid and locked. Paid runs cannot be recalculated.");
        }
        else
        {
            payRun = PayRun.Open(
                tenantId: tenantId,
                periodStart: request.PeriodStart,
                periodEnd: request.PeriodEnd,
                createdBy: actor,
                id: guid.NewGuid(),
                now: now);

            db.PayRuns.Add(payRun);
            await db.SaveChangesAsync(cancellationToken);
        }

        // ── 2. Run per-payout calculation (existing idempotent engine) ────────
        var calcResult = await sender.Send(
            new CalculatePayoutsForPeriodCommand(request.PeriodStart, request.PeriodEnd),
            cancellationToken);

        if (!calcResult.IsSuccess)
            return Result<CalculatePayRunResult>.Failure(calcResult.Error!);

        // ── 3. Assign PayRunId to newly-calculated and already-in-run payouts
        // DateOnly owned-type EF caveat: exact period equality is filtered in-memory.
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
            payRun.Id,
            calcResult.Value!.PayoutsCreated,
            calcResult.Value.Conflicts,
            calcResult.Value.Warnings));
    }
}
