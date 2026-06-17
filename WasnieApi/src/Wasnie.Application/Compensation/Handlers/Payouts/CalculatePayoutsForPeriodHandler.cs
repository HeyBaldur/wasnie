using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Wasnie.Application.Common.Abstractions;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Compensation.Commands.Payouts;
using Wasnie.Domain.Common.Results;
using Wasnie.Domain.Compensation.Assignments;
using Wasnie.Domain.Compensation.Enums;
using Wasnie.Domain.Compensation.Payouts;
using Wasnie.Domain.Compensation.Credits;
using Wasnie.Domain.Compensation.ValueObjects;

namespace Wasnie.Application.Compensation.Handlers.Payouts;

public sealed class CalculatePayoutsForPeriodHandler(
    IApplicationDbContext db,
    ITenantContext tenantContext,
    ICurrentUserService currentUser,
    IClock clock,
    IGuidGenerator guid,
    ILogger<CalculatePayoutsForPeriodHandler> logger)
    : IRequestHandler<CalculatePayoutsForPeriodCommand, Result<CalculatePayoutsResult>>
{
    public async Task<Result<CalculatePayoutsResult>> Handle(
        CalculatePayoutsForPeriodCommand request,
        CancellationToken cancellationToken)
    {
        if (request.PeriodStart > request.PeriodEnd)
            return Result<CalculatePayoutsResult>.Failure("PeriodStart must be on or before PeriodEnd.");

        var tenantId = tenantContext.TenantId;
        var actor = currentUser.Email ?? currentUser.UserId ?? "system";
        var now = clock.UtcNowOffset;

        // Load all active assignments for the tenant (in-memory DateOnly filtering — see Decision #40).
        var allAssignments = await db.PlanAssignments
            .IgnoreQueryFilters()
            .Where(a => a.TenantId == tenantId && a.Status == AssignmentStatus.Active)
            .ToListAsync(cancellationToken);

        // Optional payee filter.
        if (request.PayeeIdFilter.HasValue)
            allAssignments = allAssignments
                .Where(a => a.PayeeId == request.PayeeIdFilter.Value)
                .ToList();

        // Filter to assignments whose EffectivePeriod overlaps [PeriodStart, PeriodEnd].
        var overlapping = allAssignments
            .Where(a => a.EffectivePeriod is not null
                     && a.EffectivePeriod.Start <= request.PeriodEnd
                     && a.EffectivePeriod.End >= request.PeriodStart)
            .ToList();

        if (overlapping.Count == 0)
            return Result<CalculatePayoutsResult>.Success(
                new CalculatePayoutsResult(0, [], []));

        // Batch-load plan currencies for all relevant plan IDs (one query).
        var planIds = overlapping.Select(a => a.PlanId).Distinct().ToList();
        var planCurrencyById = (await db.CompensationPlans
            .IgnoreQueryFilters()
            .Where(p => p.TenantId == tenantId && planIds.Contains(p.Id))
            .Select(p => new { p.Id, p.Currency })
            .ToListAsync(cancellationToken))
            .ToDictionary(p => p.Id, p => p.Currency);

        var payoutsCreated = 0;
        var conflicts = new List<PayoutConflict>();
        var warnings = new List<PayoutWarning>();

        foreach (var assignment in overlapping)
        {
            // Compute intersection: payout covers only what the assignment period covers
            // within the command period.
            var intersectionStart = assignment.EffectivePeriod!.Start > request.PeriodStart
                ? assignment.EffectivePeriod.Start
                : request.PeriodStart;
            var intersectionEnd = assignment.EffectivePeriod.End < request.PeriodEnd
                ? assignment.EffectivePeriod.End
                : request.PeriodEnd;

            if (intersectionStart > intersectionEnd) continue;

            var payeeId = assignment.PayeeId;
            var planId = assignment.PlanId;
            if (!planCurrencyById.TryGetValue(planId, out var planCurrency)) continue;

            logger.LogDebug(
                "CalculatePayouts: processing payee={PayeeId}, plan={PlanId}, period={Start}–{End}",
                payeeId, planId, intersectionStart, intersectionEnd);

            // ── Idempotency check ─────────────────────────────────────────────
            // Load any existing payout for this exact (payee, plan, intersection period).
            // Use IgnoreQueryFilters because we need to see Paid/Disputed historical rows too.
            var existingPayouts = await db.CompensationPayouts
                .IgnoreQueryFilters()
                .Where(p => p.TenantId == tenantId
                         && p.PayeeId == payeeId
                         && p.PlanId == planId)
                .ToListAsync(cancellationToken);

            // Filter to same period in-memory (DateOnly owned-type EF Core translation caveat).
            var existingForPeriod = existingPayouts
                .Where(p => p.Period.Start == intersectionStart && p.Period.End == intersectionEnd)
                .ToList();

            var blocking = existingForPeriod
                .Where(p => p.Status is CompensationPayoutStatus.Approved
                                     or CompensationPayoutStatus.Paid)
                .FirstOrDefault();

            if (blocking is not null)
            {
                conflicts.Add(new PayoutConflict(
                    payeeId, assignment.PayeeSnapshot.FullName,
                    planId, intersectionStart, intersectionEnd,
                    blocking.Status.ToString()));
                logger.LogInformation(
                    "CalculatePayouts: conflict for payee={PayeeId}, plan={PlanId} — status={Status}, skipping.",
                    payeeId, planId, blocking.Status);
                continue;
            }

            // Calculated payout for same period: remove to recreate (re-run).
            var staleCalculated = existingForPeriod
                .Where(p => p.Status == CompensationPayoutStatus.Calculated)
                .ToList();

            foreach (var stale in staleCalculated)
            {
                db.CompensationPayouts.Remove(stale);
                logger.LogDebug(
                    "CalculatePayouts: removing stale Calculated payout {PayoutId} for re-run.",
                    stale.Id);
            }

            if (staleCalculated.Count > 0)
                await db.SaveChangesAsync(cancellationToken);

            // ── Aggregation audit: load Credits via two safe queries ──────────
            // Step 1: transaction IDs in the intersection period for this payee.
            // CARTESIAN GUARD: straightforward PK lookup — no fan-out join.
            var txIdsInPeriod = await db.CompensationTransactions
                .IgnoreQueryFilters()
                .Where(t => t.TenantId == tenantId
                         && t.PayeeId == payeeId
                         && t.TransactionDate >= intersectionStart
                         && t.TransactionDate <= intersectionEnd
                         && t.Amount.Currency == planCurrency)
                .Select(t => t.Id)
                .ToListAsync(cancellationToken);

            // Step 2: Credits for those transactions filtered by plan, not superseded, and not
            // consumed by a prior Paid payout (anti-double-pay: ConsumedAt == null guard).
            List<Credit> credits = [];
            if (txIdsInPeriod.Count > 0)
            {
                credits = await db.Credits
                    .IgnoreQueryFilters()
                    .Where(c => c.TenantId == tenantId
                             && c.PayeeId == payeeId
                             && c.PlanId == planId
                             && c.SupersededAt == null
                             && c.ConsumedAt == null
                             && txIdsInPeriod.Contains(c.TransactionId))
                    .ToListAsync(cancellationToken);
            }

            // ── Warning: pending transactions without Credits ─────────────────
            // Detect transactions in the period that are still Pending (no Credit yet).
            var pendingCount = await db.CompensationTransactions
                .IgnoreQueryFilters()
                .Where(t => t.TenantId == tenantId
                         && t.PayeeId == payeeId
                         && t.TransactionDate >= intersectionStart
                         && t.TransactionDate <= intersectionEnd
                         && t.Amount.Currency == planCurrency
                         && t.Status == CompensationTransactionStatus.Pending)
                .CountAsync(cancellationToken);

            if (pendingCount > 0)
            {
                warnings.Add(new PayoutWarning(
                    payeeId, assignment.PayeeSnapshot.FullName,
                    planId, intersectionStart, intersectionEnd, pendingCount));
                logger.LogWarning(
                    "CalculatePayouts: payee={PayeeId}, plan={PlanId} has {Count} Pending transactions " +
                    "without Credits — payout may be incomplete. Run ProcessPendingTransactions first.",
                    payeeId, planId, pendingCount);
            }

            // ── Build payout ──────────────────────────────────────────────────
            // One PayoutLineSpec per Credit = full transaction-level audit trail.
            var currency = credits.Count > 0
                ? credits[0].CreditedAmount.Currency
                : planCurrency;

            var lineSpecs = credits.Select(c => new PayoutLineSpec(
                c.Id,
                c.RuleId,
                c.RuleSnapshot.RuleName,
                c.OriginalAmount,
                c.CreditedAmount,
                [])).ToList();

            var payout = CompensationPayout.Calculate(
                tenantId,
                payeeId,
                planId,
                assignment.PayeeSnapshot,
                DateRange.Of(intersectionStart, intersectionEnd),
                lineSpecs,
                planCurrency,
                actor,
                guid.NewGuid(),
                now,
                guid.NewGuid(),
                guid.NewGuid);

            db.CompensationPayouts.Add(payout);
            await db.SaveChangesAsync(cancellationToken);

            payoutsCreated++;
            logger.LogInformation(
                "CalculatePayouts: created payout {PayoutId} for payee={PayeeId}, plan={PlanId}, " +
                "period={Start}–{End}, lines={LineCount}, total={Total} {Currency}.",
                payout.Id, payeeId, planId, intersectionStart, intersectionEnd,
                payout.Lines.Count, payout.TotalCommission.Amount, currency);
        }

        return Result<CalculatePayoutsResult>.Success(
            new CalculatePayoutsResult(payoutsCreated, conflicts, warnings));
    }
}
