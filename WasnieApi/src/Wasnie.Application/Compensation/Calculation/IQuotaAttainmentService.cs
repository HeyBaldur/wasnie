using Wasnie.Domain.Compensation.ValueObjects;

namespace Wasnie.Application.Compensation.Calculation;

/// <summary>
/// Context returned by <see cref="IQuotaAttainmentService.GetSplitContextAsync"/>.
/// Supplies the two values needed by the split-at-quota calculator: the revenue
/// already credited in the quota period before this transaction (prior cumulative),
/// and the quota target amount.
/// </summary>
public sealed record AttainmentSplitContext(
    decimal PriorCumulative,
    decimal QuotaTarget);

public interface IQuotaAttainmentService
{
    /// <summary>
    /// Returns the attainment ratio for a given payee + plan as of a specific date.
    /// The method is scoped per request and caches results — calling it multiple times
    /// with the same triple within one request incurs only one DB hit.
    /// Returns <see cref="AttainmentPercentage.Zero"/> when no matching active/closed quota exists.
    /// Used by bracket-lookup attainment rules (<c>SplitAtQuota = false</c>).
    /// </summary>
    Task<AttainmentPercentage> ComputeAsync(
        Guid payeeId,
        Guid planId,
        DateOnly asOfDate,
        CancellationToken ct = default);

    /// <summary>
    /// Returns the split context needed for split-at-quota attainment rules
    /// (<c>SplitAtQuota = true</c>). Returns null when no active/closed quota exists
    /// for the given payee + plan + date, which signals zero commission (Phase 5 guard).
    /// Results are NOT cached because the prior cumulative changes after each transaction
    /// is saved to DB.
    /// </summary>
    Task<AttainmentSplitContext?> GetSplitContextAsync(
        Guid payeeId,
        Guid planId,
        DateOnly asOfDate,
        CancellationToken ct = default);
}
