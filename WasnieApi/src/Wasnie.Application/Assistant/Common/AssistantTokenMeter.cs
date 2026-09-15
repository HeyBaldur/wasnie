using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Common.Interfaces;

namespace Wasnie.Application.Assistant.Common;

/// <summary>
/// How many assistant tokens an account has consumed (KAN-80) — the ONE sum both the trial allowance and the screens read.
///
/// ★ ONE QUERY, TWO READERS. The allowance that refuses a turn and the meter the user looks at must never disagree: a
/// screen saying "40% used" while the assistant says "limit reached" is a bug report waiting to happen. Both call this.
///
/// ★ INPUT + OUTPUT, AS DECIDED. The user sees one combined figure; input and output stay separate in the rows for
/// reconciliation. A call whose provider reported no usage adds nothing here — it is recorded with null tokens, and
/// those rows are countable on their own when reconciling.
/// </summary>
public static class AssistantTokenMeter
{
    /// <param name="since">Only calls from this moment on (a paying account's current billing period). Null = all.</param>
    public static async Task<long> UsedAsync(
        IApplicationDbContext db, Guid tenantId, DateTimeOffset? since, CancellationToken cancellationToken)
    {
        var rows = db.AssistantTokenUsages.Where(u => u.TenantId == tenantId);

        if (since is { } from)
        {
            rows = rows.Where(u => u.CreatedAt >= from);
        }

        return await rows.SumAsync(
            u => (long)(u.PromptTokens ?? 0) + (long)(u.CompletionTokens ?? 0), cancellationToken);
    }
}
