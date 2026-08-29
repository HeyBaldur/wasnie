using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Wasnie.Application.Common.Abstractions;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Compensation.Commands.Payouts;
using Wasnie.Domain.Common.Results;
using Wasnie.Domain.Compensation.Assignments;
using Wasnie.Domain.Compensation.Enums;
using Wasnie.Domain.Compensation.Payees;
using Wasnie.Domain.Compensation.Payouts;
using Wasnie.Domain.Compensation.Plans;
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

        // Nothing to consider. Reported as its own answer rather than folded into "0 payouts": a run
        // that had no assignments to process is a different fact from one that processed some and
        // discarded them, and the screen must be able to say which happened.
        if (overlapping.Count == 0)
            return Result<CalculatePayoutsResult>.Success(
                new CalculatePayoutsResult(0, [], [], PayoutRunDiagnostics.NothingToConsider));

        // ── The counters ──────────────────────────────────────────────────────────────────────────────
        // ★ REPORTING ONLY. Every discard below already happened exactly like this; what is new is that
        // the caller is told. The screen used to turn a zero into "No matching credits found for this
        // period" — a cause the engine never established, and in the run that prompted this work, false
        // twice: nothing was skipped for want of credits, and no credit was ever looked at.
        var assignmentsConsidered = overlapping.Count;
        var skippedTerminated = 0;
        var skippedPlanNotPayable = 0;
        var skippedExistingPayout = 0;
        var assignmentsReachingCreditLookup = 0;
        var creditsExamined = 0;

        // ── The circuit breaker for people who have left ──────────────────────────────────────────
        // A terminated payee earns nothing further, so generating payouts for them produces a ghost the
        // engine re-processes every run — and, worse, an outstanding clawback balance that keeps looking
        // like it might still be collected from future commissions that will never exist.
        //
        // The switch lives HERE, reading the Payee aggregate, and NOT in the ledger: a ledger is a record
        // of financial events, not of employment status, and freezing it with a mutable flag would break
        // the append-only rule the whole subsystem rests on. Nothing is written or erased — the debt stays
        // exactly where it is, visible on the balance and in the terminated-with-balance list, waiting for
        // a person in finance to close it. Wasnie freezes and records; it does not collect.
        //
        // Terminating someone does NOT cancel a payout already calculated for work they did: this filter
        // only stops NEW ones from being generated. A residual payout that already exists is still paid,
        // and still nets against their debt at settlement — which is the last chance to recover it.
        var payeeIds = overlapping.Select(a => a.PayeeId).Distinct().ToList();
        var terminatedPayeeIds = (await db.Payees
                .IgnoreQueryFilters()
                .Where(p => p.TenantId == tenantId
                         && payeeIds.Contains(p.Id)
                         && p.Status == PayeeStatus.Terminated)
                .Select(p => p.Id)
                .ToListAsync(cancellationToken))
            .ToHashSet();

        if (terminatedPayeeIds.Count > 0)
        {
            var before = overlapping.Count;
            overlapping = overlapping.Where(a => !terminatedPayeeIds.Contains(a.PayeeId)).ToList();
            skippedTerminated = before - overlapping.Count;
            logger.LogInformation(
                "CalculatePayouts: skipped {SkippedAssignments} assignment(s) of {TerminatedCount} terminated payee(s).",
                skippedTerminated, terminatedPayeeIds.Count);

            // This used to be the quietest exit in the engine: zero payouts, zero conflicts, zero
            // warnings — indistinguishable from a tenant with nothing to do, while a departed payee's
            // commission sat unpaid. The count now travels with it.
            if (overlapping.Count == 0)
                return Result<CalculatePayoutsResult>.Success(
                    new CalculatePayoutsResult(0, [], [], BuildDiagnostics()));
        }

        // Batch-load plan currencies for all relevant plan IDs (one query).
        // Defense in depth: exclude Archived plans. An archived plan must never contribute to a
        // payout, even if credits were somehow attributed to it — assignments for archived plans
        // fall out of this dictionary and are skipped by the TryGetValue guard in the loop below.
        var planIds = overlapping.Select(a => a.PlanId).Distinct().ToList();
        var planCurrencyById = (await db.CompensationPlans
            .IgnoreQueryFilters()
            .Where(p => p.TenantId == tenantId && planIds.Contains(p.Id) && p.Status != PlanStatus.Archived)
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

            // ★ DELIBERATELY UNCOUNTED. This guard cannot fire: the overlap filter above already proved
            // a.Start <= PeriodEnd and a.End >= PeriodStart, and DateRange.Of enforces Start <= End on
            // both ranges, so max(starts) <= min(ends) always holds. A counter here would be a number
            // that is structurally always zero — worse than absent, because a reader would take its
            // presence as evidence the case can happen. It stays as defence in depth, unreported.
            if (intersectionStart > intersectionEnd) continue;

            var payeeId = assignment.PayeeId;
            var planId = assignment.PlanId;
            // Archived or unreadable plan: no currency, so no payout this engine could defend.
            if (!planCurrencyById.TryGetValue(planId, out var planCurrency))
            {
                skippedPlanNotPayable++;
                continue;
            }

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
                skippedExistingPayout++;
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

            // ★ PAST EVERY GATE — from here the engine is actually looking for money to pay. While this
            // stays at zero for a whole run, "no matching credits" is not a statement anybody may make:
            // no credit was queried.
            assignmentsReachingCreditLookup++;

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
                             // ★★ MONEY-CLOSED. A credit closed with the departed payee's account was
                             // either settled outside Wasnie or written off; letting it back into a run
                             // pays it a second time, or pays something the company decided not to pay.
                             // Terminal, so it never comes back on its own.
                             && c.ClosedAt == null
                             && txIdsInPeriod.Contains(c.TransactionId))
                    .ToListAsync(cancellationToken);
            }

            creditsExamined += credits.Count;

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
            new CalculatePayoutsResult(payoutsCreated, conflicts, warnings, BuildDiagnostics()));

        // Reasons that discarded nothing are left OUT rather than sent as zeros: the reader needs what
        // happened, and a screen looping over a list of zeros would have to filter them out again.
        PayoutRunDiagnostics BuildDiagnostics()
        {
            var skipped = new List<PayoutSkipCount>();

            if (skippedTerminated > 0)
                skipped.Add(new PayoutSkipCount(PayoutSkipReason.TerminatedPayee, skippedTerminated));
            if (skippedPlanNotPayable > 0)
                skipped.Add(new PayoutSkipCount(PayoutSkipReason.PlanNotPayable, skippedPlanNotPayable));
            if (skippedExistingPayout > 0)
                skipped.Add(new PayoutSkipCount(PayoutSkipReason.ExistingPayout, skippedExistingPayout));

            return new PayoutRunDiagnostics(
                assignmentsConsidered,
                assignmentsReachingCreditLookup,
                creditsExamined,
                skipped);
        }
    }
}
