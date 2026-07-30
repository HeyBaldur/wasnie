using Wasnie.Domain.Common.Results;
using Wasnie.Domain.Compensation.Enums;
using Wasnie.Domain.Compensation.Quotas;
using Wasnie.Domain.Compensation.ValueObjects;
using CompensationPlan = Wasnie.Domain.Compensation.Plans.Plan;

namespace Wasnie.Application.Compensation.Common;

/// <summary>
/// The ONE place a Quota is validated and constructed, shared by the single-create and bulk-create
/// handlers.
///
/// It exists so that "the bulk path validates exactly like the individual path" is a structural fact
/// rather than a promise someone has to keep. If the two had their own copies of these three steps,
/// a rule added to one would silently not apply to the other — and a bulk endpoint that is one rule
/// laxer than the form it replaces is a way to inject data the form would have refused.
///
/// Everything here already existed in CreateQuotaHandler and was moved, not written: the period
/// containment guard, the plan-currency check inside <see cref="Quota.Create"/>, and the notes
/// length. NOTHING was added — a bulk-only rule would be exactly the anti-pattern this guards.
/// </summary>
public static class QuotaBuilder
{
    /// <summary>
    /// Validates one quota against its plan and builds it, or returns the reason it cannot exist.
    ///
    /// The caller supplies <paramref name="amount"/> and <paramref name="period"/> already built,
    /// because in a bulk they are shared by every payee in the batch and are constructed once — and
    /// because that keeps the failure surface identical to the single path, where a malformed amount
    /// or period fails before this point rather than through it.
    /// </summary>
    public static Result<Quota> Build(
        CompensationPlan plan,
        Guid tenantId,
        Guid payeeId,
        Money amount,
        DateRange period,
        QuotaMeasurementType measurementType,
        string? notes,
        string createdBy,
        Guid id,
        DateTimeOffset now)
    {
        // Integrity guard: the quota period must fall within the plan's effective period. The UI
        // defaults to the plan period and validates it client-side, but a direct API call must not be
        // able to set a window outside the plan that attainment is measured against. Shared with
        // UpdateQuotaHandler via QuotaPeriodGuard so no path can bypass the rule.
        var periodError = QuotaPeriodGuard.Validate(plan.EffectivePeriod, period.Start, period.End);
        if (periodError is not null)
            return Result<Quota>.Failure(periodError);

        // Defensive copies of the owned value objects, one fresh instance per quota.
        //
        // `Money` and `DateRange` are EF OWNED types: an owned instance belongs to exactly one owner.
        // Handing the SAME Money object to several quotas makes the change tracker treat it as one
        // owned entity with several owners, and every insert after the first writes NULL into
        // QuotaAmount — a NOT NULL column, so the batch dies at SaveChanges. (Caught here by the
        // bulk tests; the same trap is documented in CreditAllocationService, which copies
        // transaction.Amount for exactly this reason.)
        //
        // Not a validation and not a behaviour change: the copy is built from the same values through
        // the same factories, so the single-create path constructs precisely what it did before.
        var ownAmount = Money.OfNonNegative(amount.Amount, amount.Currency);
        var ownPeriod = DateRange.Of(period.Start, period.End);

        try
        {
            return Result<Quota>.Success(Quota.Create(
                tenantId,
                payeeId,
                plan.Id,
                ownAmount,
                ownPeriod,
                measurementType,
                createdBy,
                id,
                now,
                notes,
                planCurrency: plan.Currency));
        }
        catch (Exception ex)
        {
            return Result<Quota>.Failure(ex.Message);
        }
    }
}
