using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Domain.Compensation.Enums;

namespace Wasnie.Application.Compensation.Common;

/// <summary>The decision for one candidate transaction key against current DB state.</summary>
public enum TransactionCreateDecision
{
    /// <summary>No ACTIVE row has this key → create (even if a voided row with the key exists — Opción B).</summary>
    Create,

    /// <summary>An ACTIVE (non-Cancelled) row already has this key → real duplicate, skip.</summary>
    SkipActiveDuplicate,

    /// <summary>
    /// Only a voided row has this key, but that voided row carries credit history → re-creating could
    /// open a double-pay path. Anti-double-pay is sacred (Rule 10): block and explain, never create.
    /// </summary>
    BlockedVoidHadCredits,

    /// <summary>
    /// The only blocking row was a CRM transaction cancelled BECAUSE its deal was lost, whose commission was
    /// NEVER paid (superseded, unconsumed). The deal has now returned, so re-creating is CORRECT (the sale is
    /// real again) and safe from double-pay (nothing was ever paid on it). The caller creates a fresh
    /// transaction and treats it as a deal RECOVERY (audit + resolve the stale deal-lost alert).
    /// </summary>
    RecreateAfterDealLost,
}

/// <summary>
/// Snapshot classification of candidate keys vs the DB, built in ONE batch (no N+1) and consulted
/// in-memory per row. This is THE single place the "can I create this transaction?" rule lives — HubSpot,
/// Excel and Manual all consult it, instead of each importer re-implementing duplicate detection.
/// </summary>
public sealed class TransactionCreateClassification
{
    private readonly IReadOnlySet<string> _activeReferences;
    private readonly IReadOnlySet<string> _activeExternalIds;
    private readonly IReadOnlySet<string> _blockedReferences;
    private readonly IReadOnlySet<string> _blockedExternalIds;
    private readonly IReadOnlySet<string> _recoverableReferences;
    private readonly IReadOnlySet<string> _recoverableExternalIds;

    internal TransactionCreateClassification(
        IReadOnlySet<string> activeReferences,
        IReadOnlySet<string> activeExternalIds,
        IReadOnlySet<string> blockedReferences,
        IReadOnlySet<string> blockedExternalIds,
        IReadOnlySet<string> recoverableReferences,
        IReadOnlySet<string> recoverableExternalIds)
    {
        _activeReferences = activeReferences;
        _activeExternalIds = activeExternalIds;
        _blockedReferences = blockedReferences;
        _blockedExternalIds = blockedExternalIds;
        _recoverableReferences = recoverableReferences;
        _recoverableExternalIds = recoverableExternalIds;
    }

    public TransactionCreateDecision Decide(string referenceNumber, string? externalId)
    {
        // Order matters: an ACTIVE row wins (idempotent skip — this is what makes recovery idempotent, since
        // once re-created the new active row is found here on the next sync).
        if (_activeReferences.Contains(referenceNumber)
            || (externalId is not null && _activeExternalIds.Contains(externalId)))
            return TransactionCreateDecision.SkipActiveDuplicate;

        // A deal-lost cancellation whose commission was never paid → re-create (the deal came back).
        if (_recoverableReferences.Contains(referenceNumber)
            || (externalId is not null && _recoverableExternalIds.Contains(externalId)))
            return TransactionCreateDecision.RecreateAfterDealLost;

        if (_blockedReferences.Contains(referenceNumber)
            || (externalId is not null && _blockedExternalIds.Contains(externalId)))
            return TransactionCreateDecision.BlockedVoidHadCredits;

        return TransactionCreateDecision.Create;
    }
}

/// <summary>
/// Builds a <see cref="TransactionCreateClassification"/> for a set of candidate keys. Tenant-scoped via the
/// ambient query filter (Rule 9). Reference uniqueness is tenant-wide (any source); external-id idempotency
/// is per source.
/// </summary>
public interface ITransactionCreateGuard
{
    Task<TransactionCreateClassification> ClassifyAsync(
        TransactionSource source,
        IReadOnlyCollection<string> referenceNumbers,
        IReadOnlyCollection<string?> externalIds,
        CancellationToken cancellationToken = default);
}

public sealed class TransactionCreateGuard(IApplicationDbContext db) : ITransactionCreateGuard
{
    private static readonly IReadOnlySet<string> Empty = new HashSet<string>(StringComparer.Ordinal);

    public async Task<TransactionCreateClassification> ClassifyAsync(
        TransactionSource source,
        IReadOnlyCollection<string> referenceNumbers,
        IReadOnlyCollection<string?> externalIds,
        CancellationToken cancellationToken = default)
    {
        var refSet = referenceNumbers.Where(r => !string.IsNullOrWhiteSpace(r)).Distinct().ToList();
        var extSet = externalIds.Where(e => !string.IsNullOrWhiteSpace(e)).Select(e => e!).Distinct().ToList();

        // Active (non-Cancelled) rows with these references — Reference is unique per tenant across sources.
        var activeRefs = refSet.Count == 0 ? Empty : (await db.CompensationTransactions
            .Where(t => t.Status != CompensationTransactionStatus.Cancelled && refSet.Contains(t.ReferenceNumber))
            .Select(t => t.ReferenceNumber)
            .ToListAsync(cancellationToken)).ToHashSet(StringComparer.Ordinal);

        // Active rows with these external ids FOR THIS SOURCE — external idempotency is per source.
        var activeExts = extSet.Count == 0 ? Empty : (await db.CompensationTransactions
            .Where(t => t.Status != CompensationTransactionStatus.Cancelled && t.Source == source
                     && t.ExternalId != null && extSet.Contains(t.ExternalId))
            .Select(t => t.ExternalId!)
            .ToListAsync(cancellationToken)).ToHashSet(StringComparer.Ordinal);

        // Shield: among keys with NO active row, find voided rows that carry ANY credit history.
        // Re-creating from those is blocked (anti-double-pay). Conservative: any credit row, active or
        // superseded. Normal voids (Pending→Cancelled) have zero credits, so this rarely triggers.
        var refsToCheck = refSet.Where(r => !activeRefs.Contains(r)).ToList();
        var blockedRefs = refsToCheck.Count == 0 ? Empty : (await db.CompensationTransactions
            .Where(t => t.Status == CompensationTransactionStatus.Cancelled
                     && refsToCheck.Contains(t.ReferenceNumber)
                     && db.Credits.Any(c => c.TransactionId == t.Id))
            .Select(t => t.ReferenceNumber)
            .ToListAsync(cancellationToken)).ToHashSet(StringComparer.Ordinal);

        var extsToCheck = extSet.Where(e => !activeExts.Contains(e)).ToList();
        var blockedExts = extsToCheck.Count == 0 ? Empty : (await db.CompensationTransactions
            .Where(t => t.Status == CompensationTransactionStatus.Cancelled && t.Source == source
                     && t.ExternalId != null && extsToCheck.Contains(t.ExternalId)
                     && db.Credits.Any(c => c.TransactionId == t.Id))
            .Select(t => t.ExternalId!)
            .ToListAsync(cancellationToken)).ToHashSet(StringComparer.Ordinal);

        // Recovery (lost→won): a blocked row that was cancelled BECAUSE its deal was lost, and whose
        // commission was NEVER paid (no consumed credit — deal-lost credits are superseded + unconsumed),
        // may be re-created. Only for CRM sync (deal-lost cancellations are CrmSync); manual/Excel keep the
        // strict block. The consumed-credit exclusion is the anti-double-pay guard (Rule 10): if any credit
        // was ever paid, it stays blocked and is never silently re-credited.
        var recoverableRefs = Empty;
        var recoverableExts = Empty;
        if (source == TransactionSource.CrmSync)
        {
            var prefix = Domain.Compensation.Transactions.CompensationTransaction.DealLostCancellationReasonPrefix;

            if (blockedRefs.Count > 0)
                recoverableRefs = (await db.CompensationTransactions
                    .Where(t => t.Status == CompensationTransactionStatus.Cancelled
                             && blockedRefs.Contains(t.ReferenceNumber)
                             && t.CancelledReason != null && t.CancelledReason.StartsWith(prefix)
                             && !db.Credits.Any(c => c.TransactionId == t.Id && c.ConsumedAt != null))
                    .Select(t => t.ReferenceNumber)
                    .ToListAsync(cancellationToken)).ToHashSet(StringComparer.Ordinal);

            if (blockedExts.Count > 0)
                recoverableExts = (await db.CompensationTransactions
                    .Where(t => t.Status == CompensationTransactionStatus.Cancelled && t.Source == source
                             && t.ExternalId != null && blockedExts.Contains(t.ExternalId)
                             && t.CancelledReason != null && t.CancelledReason.StartsWith(prefix)
                             && !db.Credits.Any(c => c.TransactionId == t.Id && c.ConsumedAt != null))
                    .Select(t => t.ExternalId!)
                    .ToListAsync(cancellationToken)).ToHashSet(StringComparer.Ordinal);

            // A recoverable key must NOT also be reported as blocked.
            if (recoverableRefs.Count > 0)
                blockedRefs = blockedRefs.Where(r => !recoverableRefs.Contains(r)).ToHashSet(StringComparer.Ordinal);
            if (recoverableExts.Count > 0)
                blockedExts = blockedExts.Where(e => !recoverableExts.Contains(e)).ToHashSet(StringComparer.Ordinal);
        }

        return new TransactionCreateClassification(
            activeRefs, activeExts, blockedRefs, blockedExts, recoverableRefs, recoverableExts);
    }
}
