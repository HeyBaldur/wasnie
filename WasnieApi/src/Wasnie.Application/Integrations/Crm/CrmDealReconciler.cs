using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Common.Abstractions;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Compensation.Common;
using Wasnie.Application.Integrations.Crm.Drift;
using Wasnie.Domain.Compensation.Enums;
using Wasnie.Domain.Compensation.Transactions;
using Wasnie.Domain.Compensation.ValueObjects;
using Wasnie.Domain.Exceptions;

namespace Wasnie.Application.Integrations.Crm;

/// <inheritdoc cref="ICrmDealReconciler"/>
public sealed class CrmDealReconciler(
    IApplicationDbContext db,
    IGuidGenerator guid,
    ICrmOwnerResolver ownerResolver,
    ITransactionCreateGuard createGuard,
    ICrmDriftPolicy driftPolicy)
    : ICrmDealReconciler
{
    public async Task<CrmSyncResult> ReconcileAsync(
        Guid tenantId,
        string sourceName,
        IReadOnlyList<CrmDeal> deals,
        IReadOnlyList<CrmOwner> owners,
        string? defaultCurrency,
        string actor,
        string actorEmail,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        var ownerEmailById = owners
            .GroupBy(o => o.Id, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().Email, StringComparer.Ordinal);

        // Centralized create rule (shared with Excel/Manual): classify each deal's key against the DB.
        // ACTIVE row with this deal → skip (idempotent). Only a VOID row → create a NEW one (Opción B),
        // unless that void carried credits (anti-double-pay → blocked). Status-aware, so re-importing a
        // previously voided deal now revives it as a fresh transaction.
        var dealIds = deals.Where(d => !string.IsNullOrWhiteSpace(d.Id)).Select(d => d.Id).ToList();
        var dealReferences = dealIds.Select(id => $"HUBSPOT-{id}").ToList();
        var classification = await createGuard.ClassifyAsync(
            TransactionSource.CrmSync, dealReferences, dealIds.Cast<string?>().ToList(), cancellationToken);

        // DRIFT DETECTION: load the ACTIVE transaction for each already-imported deal so we can compare the
        // deal's current amount/close-date against what Wasnie stored. The filtered unique index guarantees
        // at most one active row per (tenant, CrmSync, externalId), so First() is safe.
        var activeTxByDealId = (await db.CompensationTransactions
                .Where(t => t.Status != CompensationTransactionStatus.Cancelled
                         && t.Source == TransactionSource.CrmSync
                         && t.ExternalId != null
                         && dealIds.Contains(t.ExternalId))
                .ToListAsync(cancellationToken))
            .GroupBy(t => t.ExternalId!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        // Dedups the same deal appearing twice within THIS batch (the classification only knows the DB).
        var seenInBatch = new HashSet<string>(StringComparer.Ordinal);

        var today = DateOnly.FromDateTime(now.UtcDateTime);

        int created = 0, assigned = 0, unassigned = 0, skippedExisting = 0, skippedInvalid = 0, newMappings = 0, skippedBlocked = 0;
        var driftCandidates = new List<CrmDriftCandidate>();
        var missingAmount = 0;
        var missingCurrency = 0;
        var missingCloseDate = 0;

        // Resolve each distinct owner once, caching the result (and any new email-match mapping).
        var ownerResolutionCache = new Dictionary<string, CrmOwnerResolution>(StringComparer.Ordinal);

        async Task<Guid?> ResolvePayeeAsync(string? ownerId)
        {
            if (string.IsNullOrEmpty(ownerId))
                return null;
            if (!ownerResolutionCache.TryGetValue(ownerId, out var resolution))
            {
                ownerEmailById.TryGetValue(ownerId, out var email);
                resolution = await ownerResolver.ResolveAsync(tenantId, sourceName, ownerId, email, cancellationToken);
                ownerResolutionCache[ownerId] = resolution;
                if (resolution.NewMappingCreated)
                    newMappings++;
            }
            return resolution.PayeeId;
        }

        // Builds the comparable (amount+currency, close date) for an already-imported deal. Mirrors the
        // create-path validation. Missing close date stays null (a missing date must NOT look like drift).
        static bool TryBuildDriftIncoming(CrmDeal deal, string? defaultCurrency, out CrmDriftIncoming incoming)
        {
            incoming = null!;
            if (deal.Amount is null)
                return false;
            var currency = deal.CurrencyCode ?? defaultCurrency;
            if (string.IsNullOrWhiteSpace(currency))
                return false;
            Money money;
            try
            {
                money = Money.Of(deal.Amount.Value, currency);
            }
            catch (DomainException)
            {
                return false;
            }
            incoming = new CrmDriftIncoming(deal.Id, money, deal.CloseDate);
            return true;
        }

        foreach (var deal in deals)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(deal.Id) || !seenInBatch.Add(deal.Id))
            {
                // Blank id (shouldn't happen) or repeated within this batch → skip.
                if (!string.IsNullOrWhiteSpace(deal.Id))
                    skippedExisting++;
                continue;
            }

            switch (classification.Decide($"HUBSPOT-{deal.Id}", deal.Id))
            {
                case TransactionCreateDecision.SkipActiveDuplicate:
                    // Not "blindly skip": the deal already exists, but it may have CHANGED in the CRM. Pair
                    // it with its active transaction and hand it to the drift policy, which decides (no drift
                    // → skip; Pending → auto-void+recreate; Calculated/Paid → alert). If we can't build
                    // comparable values or can't find the active row, fall back to the old skip.
                    if (TryBuildDriftIncoming(deal, defaultCurrency, out var incoming)
                        && activeTxByDealId.TryGetValue(deal.Id, out var activeTx))
                        driftCandidates.Add(new CrmDriftCandidate(incoming, activeTx));
                    else
                        skippedExisting++;
                    continue;
                case TransactionCreateDecision.BlockedVoidHadCredits:
                    skippedBlocked++;
                    continue;
            }

            if (deal.Amount is null)
            {
                skippedInvalid++;
                missingAmount++;
                continue;
            }

            var currency = deal.CurrencyCode ?? defaultCurrency;
            if (string.IsNullOrWhiteSpace(currency))
            {
                skippedInvalid++;
                missingCurrency++;
                continue;
            }

            Money amount;
            try
            {
                amount = Money.Of(deal.Amount.Value, currency);
            }
            catch (DomainException)
            {
                skippedInvalid++;
                continue;
            }

            var transactionDate = deal.CloseDate ?? today;
            if (deal.CloseDate is null)
                missingCloseDate++;

            var payeeId = await ResolvePayeeAsync(deal.OwnerId);

            CompensationTransaction tx;
            try
            {
                tx = CompensationTransaction.Ingest(
                    tenantId: tenantId,
                    referenceNumber: $"HUBSPOT-{deal.Id}",
                    payeeId: payeeId,
                    amount: amount,
                    transactionDate: transactionDate,
                    source: TransactionSource.CrmSync,
                    ingestedBy: actor,
                    id: guid.NewGuid(),
                    now: now,
                    eventId: guid.NewGuid(),
                    externalId: deal.Id,
                    quantity: 1);
            }
            catch (DomainException)
            {
                skippedInvalid++;
                continue;
            }

            db.CompensationTransactions.Add(tx);
            created++;
            if (payeeId.HasValue) assigned++; else unassigned++;
        }

        await db.SaveChangesAsync(cancellationToken);

        // DRIFT POLICY: for every already-imported deal, reconcile changes against the existing transaction.
        // Pending → auto-void + recreate with the new values; Calculated/Paid → record an alert, untouched.
        // A no-drift candidate is just a normal "already imported" skip, so it folds back into SkippedExisting.
        var drift = driftCandidates.Count == 0
            ? CrmDriftResult.Empty
            : await driftPolicy.ReconcileAsync(
                TransactionSource.CrmSync, sourceName, driftCandidates, now, actor, actorEmail, cancellationToken);

        skippedExisting += drift.NoDriftCount;

        return new CrmSyncResult(
            DealsRead: deals.Count,
            Created: created,
            AssignedToPayee: assigned,
            Unassigned: unassigned,
            SkippedAlreadyImported: skippedExisting,
            SkippedInvalid: skippedInvalid,
            NewOwnerMappings: newMappings,
            DriftAutoResolved: drift.AutoResolvedCount,
            DriftAlertsRaised: drift.AlertedCount,
            SkippedBlocked: skippedBlocked,
            MissingAmount: missingAmount,
            MissingCurrency: missingCurrency,
            MissingCloseDate: missingCloseDate);
    }
}
