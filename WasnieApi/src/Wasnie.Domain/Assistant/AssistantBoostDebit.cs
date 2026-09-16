using Wasnie.Domain.Common;
using Wasnie.Domain.Exceptions;

namespace Wasnie.Domain.Assistant;

/// <summary>
/// What a CLOSED billing period spent beyond the plan's included allowance, charged to the tenant's boost (KAN-83).
///
/// ★ WHY THIS ROW HAS TO EXIST, when everything else here is derived. Usage is derived from
/// <see cref="AssistantTokenUsage"/> rows and the boost from <see cref="AssistantTokenBoost"/> lots — but the included
/// allowance is a per-PERIOD number, and the only period the system knows is the current one
/// (<c>UserSubscription.CurrentPeriodStart</c>). Stripe keeps no period history for us. So the moment a period rolls
/// over is the only moment its overage can still be computed; not writing it down loses it. This row is that fact,
/// captured once, at the only time it is knowable.
///
/// ★ ONE ROW PER PERIOD, ENFORCED. The unique index on (TenantId, PeriodStart) is what makes rollover idempotent: a
/// webhook redelivered, a reconciler running twice, two instances racing — the second insert fails instead of charging
/// the customer's boost twice for the same month.
///
/// ★ ZERO IS WORTH WRITING. A period that stayed inside its allowance still writes a row, with zero tokens. It is the
/// difference between "this period cost no boost" and "this period was never closed" — and only the first one means the
/// balance below it can be trusted (§B3).
/// </summary>
public sealed class AssistantBoostDebit : Entity
{
    public Guid TenantId { get; private set; }

    /// <summary>Start of the closed period. With the tenant, the natural key — see the idempotency note above.</summary>
    public DateTimeOffset PeriodStart { get; private set; }

    public DateTimeOffset PeriodEnd { get; private set; }

    /// <summary>The included allowance that applied to that period, frozen — the setting can change later.</summary>
    public long IncludedLimit { get; private set; }

    /// <summary>Everything the tenant spent in the period, included allowance and boost together.</summary>
    public long UsedInPeriod { get; private set; }

    /// <summary>The part of <see cref="UsedInPeriod"/> that exceeded the allowance. Zero is a valid, meaningful value.</summary>
    public long Tokens { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    private AssistantBoostDebit() { }

    public static AssistantBoostDebit Close(
        Guid id,
        Guid tenantId,
        DateTimeOffset periodStart,
        DateTimeOffset periodEnd,
        long includedLimit,
        long usedInPeriod,
        DateTimeOffset now)
    {
        if (tenantId == Guid.Empty)
            throw new DomainException("TenantId must not be empty.");
        if (periodEnd <= periodStart)
            throw new DomainException("A period must end after it starts.");
        if (includedLimit < 0)
            throw new DomainException("The included allowance cannot be negative.");
        if (usedInPeriod < 0)
            throw new DomainException("Usage in a period cannot be negative.");

        return new AssistantBoostDebit
        {
            Id = id,
            TenantId = tenantId,
            PeriodStart = periodStart,
            PeriodEnd = periodEnd,
            IncludedLimit = includedLimit,
            UsedInPeriod = usedInPeriod,
            // The included allowance is spent FIRST, so only what is left over touches the boost the tenant paid for.
            Tokens = Math.Max(0, usedInPeriod - includedLimit),
            CreatedAt = now,
        };
    }
}
