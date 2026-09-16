using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Wasnie.Application.Common.Abstractions;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Common.Options;
using Wasnie.Domain.Assistant;

namespace Wasnie.Application.Assistant.Common;

/// <summary>
/// Writes down what a billing period spent past its included tokens, at the one moment it is still knowable (KAN-83).
///
/// ★ WHY IT RUNS AT ROLLOVER AND NOWHERE ELSE. The included allowance is per period, and the only period the system
/// stores is the CURRENT one. The instant Stripe moves the period on, the old window's boundaries are gone — so its
/// overage is computed and recorded here, before the subscription row is updated, or it is lost for good.
///
/// ★ NOTHING IS SAVED HERE. The row is added to the change tracker and committed by the caller's own
/// SaveChangesAsync, so the period's charge and the subscription's new period land together or not at all. A charge
/// without the rollover would be applied twice next time; a rollover without the charge would hand the tenant a free
/// month of overage.
/// </summary>
public sealed class AssistantPeriodCloser(
    IApplicationDbContext db,
    IOptions<BillingOptions> billingOptions,
    IClock clock,
    ILogger<AssistantPeriodCloser> logger)
    : IAssistantPeriodCloser
{
    public async Task CloseIfRolledOverAsync(
        Guid tenantId,
        DateTimeOffset? previousPeriodStart,
        DateTimeOffset? previousPeriodEnd,
        DateTimeOffset newPeriodStart,
        CancellationToken cancellationToken = default)
    {
        if (previousPeriodStart is not { } from)
            return; // Nothing has been billed yet: there is no closed period to charge.

        if (newPeriodStart <= from)
            return; // The same period, re-announced. Stripe redelivers, and closing twice would double-charge.

        // The window ends where the new one begins. Stripe's own end date is preferred when it agrees; when it does
        // not, the new start is the boundary that leaves no gap and no overlap — every token lands in exactly one period.
        var to = previousPeriodEnd is { } end && end <= newPeriodStart ? end : newPeriodStart;

        var alreadyClosed = await db.AssistantBoostDebits
            .IgnoreQueryFilters()
            .AnyAsync(d => d.TenantId == tenantId && d.PeriodStart == from, cancellationToken);

        if (alreadyClosed)
            return;

        var used = await AssistantTokenMeter.UsedAsync(db, tenantId, from, cancellationToken, until: to);
        var included = billingOptions.Value.IncludedAssistantTokensPerMonth;

        var debit = AssistantBoostDebit.Close(
            Guid.NewGuid(), tenantId, from, to, included, used, clock.UtcNowOffset);

        db.AssistantBoostDebits.Add(debit);

        logger.LogInformation(
            "Tenant {TenantId} period {From:o}–{To:o} closed: {Used} tokens used of {Included} included, {Charged} charged to boost",
            tenantId, from, to, used, included, debit.Tokens);
    }
}
