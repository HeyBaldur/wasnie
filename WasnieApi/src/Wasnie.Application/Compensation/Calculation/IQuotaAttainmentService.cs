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

/// <summary>
/// An attainment ratio AND where it came from.
///
/// ★★ THE RATIO ALONE IS AMBIGUOUS AT ZERO, AND THAT AMBIGUITY WAS BEING PAID OUT. A 0 means "this
/// rep achieved none of their target" or "nobody ever set a target" — a bad quarter and a
/// configuration hole, indistinguishable in the number and opposite in what they should cause. The
/// caller cannot recover the difference afterwards, because the only evidence is at the point where
/// the quota was looked up. So it travels WITH the ratio rather than being inferred from it.
/// </summary>
/// <param name="Value">The ratio. 0 when there was nothing to measure against.</param>
/// <param name="Source">
/// <see cref="AttainmentSource.Measured"/> when a real target answered — including a real 0% — and
/// <see cref="AttainmentSource.NoTarget"/> when there was no quota in effect, or its target was 0.
/// </param>
public sealed record AttainmentReading(
    AttainmentPercentage Value,
    AttainmentSource Source);

public interface IQuotaAttainmentService
{
    /// <summary>
    /// Returns the attainment ratio for a given payee + plan as of a specific date, AND where that
    /// ratio came from. The method is scoped per request and caches results — calling it multiple
    /// times with the same triple within one request incurs only one DB hit.
    /// Used by bracket-lookup attainment rules (<c>SplitAtQuota = false</c>).
    ///
    /// ★ Returns a ratio of 0 with <see cref="AttainmentSource.NoTarget"/> when no matching
    /// active/closed quota exists, or the one that matched has a target of 0 — never a bare zero the
    /// caller would have to guess about. See <see cref="AttainmentReading"/>.
    /// </summary>
    Task<AttainmentReading> ComputeAsync(
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
