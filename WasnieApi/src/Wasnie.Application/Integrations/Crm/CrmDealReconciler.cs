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
    ICrmDriftPolicy driftPolicy,
    Wasnie.Application.Compensation.Enrichment.ITransactionEnrichmentService enrichmentService)
    : ICrmDealReconciler
{
    // Tolerance (currency minor unit) for Σ(line item amounts) vs the deal amount. Above this the deal is
    // flagged for review instead of ingested — we never invent numbers to force a match.
    private const decimal TotalsTolerance = 0.01m;

    private static string DealRef(string dealId) => $"HUBSPOT-{dealId}";
    private static string LineRef(string dealId, string lineId) => $"HUBSPOT-{dealId}-{lineId}";
    private static string LineExt(string dealId, string lineId) => $"{dealId}-{lineId}";

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

        // Enrichment: load the tenant's category lookup ONCE for the whole sync (no per-line-item query).
        var categoryResolver = await enrichmentService.LoadResolverAsync(tenantId, cancellationToken);

        var dealsWithId = deals.Where(d => !string.IsNullOrWhiteSpace(d.Id)).ToList();

        // Build ALL candidate keys in one classification pass (no N+1). Two levels:
        //  - Deal-level key ("HUBSPOT-{dealId}" / externalId {dealId}) — detects LEGACY per-deal transactions
        //    imported before the line-item migration. Their presence means forward-only coexistence (Opción 1):
        //    keep the legacy row, run its deal-level drift, and do NOT create line-item rows for that deal.
        //  - Line-item keys ("HUBSPOT-{dealId}-{lineItemId}" / externalId {dealId}-{lineItemId}) — the new scheme.
        var refs = new List<string>();
        var exts = new List<string?>();
        foreach (var d in dealsWithId)
        {
            refs.Add(DealRef(d.Id));
            exts.Add(d.Id);
            foreach (var li in d.Lines.Where(li => !string.IsNullOrWhiteSpace(li.Id)))
            {
                refs.Add(LineRef(d.Id, li.Id));
                exts.Add(LineExt(d.Id, li.Id));
            }
        }
        var classification = await createGuard.ClassifyAsync(
            TransactionSource.CrmSync, refs, exts, cancellationToken);

        // Preload active CrmSync transactions for every candidate externalId (deal-level AND line-item),
        // keyed by externalId — used for drift comparison. Filtered unique index guarantees ≤1 active per key.
        var extIdSet = exts.Where(e => e is not null).Select(e => e!).Distinct().ToList();
        var activeTxByExternalId = (await db.CompensationTransactions
                .Where(t => t.Status != CompensationTransactionStatus.Cancelled
                         && t.Source == TransactionSource.CrmSync
                         && t.ExternalId != null
                         && extIdSet.Contains(t.ExternalId))
                .ToListAsync(cancellationToken))
            .GroupBy(t => t.ExternalId!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var seenInBatch = new HashSet<string>(StringComparer.Ordinal);
        var today = DateOnly.FromDateTime(now.UtcDateTime);

        int created = 0, assigned = 0, unassigned = 0, skippedExisting = 0, skippedInvalid = 0, newMappings = 0, skippedBlocked = 0;
        var driftCandidates = new List<CrmDriftCandidate>();
        var missingAmount = 0;
        var missingCurrency = 0;
        var missingCloseDate = 0;
        var totalsMismatch = 0;

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

        foreach (var deal in dealsWithId)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!seenInBatch.Add(deal.Id))
            {
                skippedExisting++;
                continue;
            }

            var currency = deal.CurrencyCode ?? defaultCurrency;

            // ── Legacy coexistence (Opción 1, forward-only) ─────────────────────────────
            // A per-deal transaction already exists (pre-migration). Keep it: run deal-level drift and do
            // NOT create line-item rows — never re-import a deal already imported under the old scheme.
            var dealDecision = classification.Decide(DealRef(deal.Id), deal.Id);
            if (dealDecision == TransactionCreateDecision.SkipActiveDuplicate)
            {
                if (TryBuildDealDriftIncoming(deal, currency, out var legacyIncoming)
                    && activeTxByExternalId.TryGetValue(deal.Id, out var legacyTx))
                    driftCandidates.Add(new CrmDriftCandidate(legacyIncoming, legacyTx));
                else
                    skippedExisting++;
                continue;
            }
            if (dealDecision == TransactionCreateDecision.BlockedVoidHadCredits)
            {
                skippedBlocked++;
                continue;
            }

            // ── New deal WITHOUT line items → one transaction (unchanged legacy behaviour) ──
            if (deal.Lines.Count == 0)
            {
                if (deal.Amount is null) { skippedInvalid++; missingAmount++; continue; }
                if (string.IsNullOrWhiteSpace(currency)) { skippedInvalid++; missingCurrency++; continue; }

                Money dealAmount;
                try { dealAmount = Money.Of(deal.Amount.Value, currency); }
                catch (DomainException) { skippedInvalid++; continue; }

                var dealDate = deal.CloseDate ?? today;
                if (deal.CloseDate is null) missingCloseDate++;
                var dealPayeeId = await ResolvePayeeAsync(deal.OwnerId);

                try
                {
                    var dealTx = CompensationTransaction.Ingest(
                        tenantId: tenantId, referenceNumber: DealRef(deal.Id), payeeId: dealPayeeId,
                        amount: dealAmount, transactionDate: dealDate, source: TransactionSource.CrmSync,
                        ingestedBy: actor, id: guid.NewGuid(), now: now, eventId: guid.NewGuid(),
                        externalId: deal.Id, quantity: 1, description: deal.Name);
                    db.CompensationTransactions.Add(dealTx);
                    created++;
                    if (dealPayeeId.HasValue) assigned++; else unassigned++;
                }
                catch (DomainException) { skippedInvalid++; }
                continue;
            }

            // ── New deal WITH line items → one transaction per line item ─────────────────
            if (deal.Amount is null) { skippedInvalid++; missingAmount++; continue; }
            if (string.IsNullOrWhiteSpace(currency)) { skippedInvalid++; missingCurrency++; continue; }

            // Totals guard: Σ(line item amount) must reconcile with the deal amount. A gap (deal-level
            // discount/tax/override) is flagged for review — we do NOT prorate or invent numbers.
            var lineSum = deal.Lines.Sum(li => li.Amount ?? 0m);
            if (Math.Abs(lineSum - deal.Amount.Value) > TotalsTolerance)
            {
                totalsMismatch++;
                continue; // signalled, not ingested
            }

            var transactionDate = deal.CloseDate ?? today;
            if (deal.CloseDate is null) missingCloseDate++;
            var payeeId = await ResolvePayeeAsync(deal.OwnerId);

            foreach (var li in deal.Lines)
            {
                if (string.IsNullOrWhiteSpace(li.Id)) { skippedInvalid++; continue; }

                var lineRef = LineRef(deal.Id, li.Id);
                var lineExt = LineExt(deal.Id, li.Id);

                var lineDecision = classification.Decide(lineRef, lineExt);
                if (lineDecision == TransactionCreateDecision.SkipActiveDuplicate)
                {
                    // Already imported this line item → drift-check the existing line-item transaction
                    // (amount/close-date compared per line). Idempotent: no duplicate row.
                    if (TryBuildLineDriftIncoming(deal, li, currency, out var lineIncoming)
                        && activeTxByExternalId.TryGetValue(lineExt, out var lineTx))
                        driftCandidates.Add(new CrmDriftCandidate(lineIncoming, lineTx));
                    else
                        skippedExisting++;
                    continue;
                }
                if (lineDecision == TransactionCreateDecision.BlockedVoidHadCredits) { skippedBlocked++; continue; }

                if (li.Amount is null) { skippedInvalid++; continue; }

                // Quantity must be a whole number ≥ 1 (the domain enforces ≥ 1). Round HubSpot's decimal
                // quantity; a non-positive/fractional-to-zero quantity is invalid, not silently coerced.
                var qty = li.Quantity is { } q && q >= 0.5m ? (int)Math.Round(q, MidpointRounding.AwayFromZero) : 0;

                Money lineAmount;
                try { lineAmount = Money.Of(li.Amount.Value, currency); }
                catch (DomainException) { skippedInvalid++; continue; }

                CompensationTransaction tx;
                try
                {
                    tx = CompensationTransaction.Ingest(
                        tenantId: tenantId, referenceNumber: lineRef, payeeId: payeeId,
                        amount: lineAmount, transactionDate: transactionDate, source: TransactionSource.CrmSync,
                        ingestedBy: actor, id: guid.NewGuid(), now: now, eventId: guid.NewGuid(),
                        externalId: lineExt, quantity: qty,
                        // Description stays the DEAL name — every line of a deal shares it, and it is
                        // what identifies the sale. The line item's own name and SKU say WHAT was sold
                        // and go in their own fields, so a deal mixing (say) machinery and its
                        // installation course keeps one sale label and two distinct products.
                        description: deal.Name,
                        productName: li.Name,
                        productSku: li.Sku,
                        // WI-CRM-CATEGORY precedence: the category the CRM already carries WINS; only when it
                        // is absent do we fall back to the manual lookup table (SKU→Name, resolved in-memory,
                        // no DB call per line). CategoryResolver's own logic is untouched — the CRM value is
                        // merely prepended. Ingest normalizes the final value (trim / blank → null).
                        category: string.IsNullOrWhiteSpace(li.CategoryFromCrm)
                            ? categoryResolver.Resolve(li.Sku, li.Name)
                            : li.CategoryFromCrm);
                }
                catch (DomainException) { skippedInvalid++; continue; }

                db.CompensationTransactions.Add(tx);
                created++;
                if (payeeId.HasValue) assigned++; else unassigned++;
            }
        }

        await db.SaveChangesAsync(cancellationToken);

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
            MissingCloseDate: missingCloseDate,
            TotalsMismatch: totalsMismatch);
    }

    // Deal-level drift comparable (legacy per-deal transactions). Missing close date stays null so the
    // importer's "today" substitution never masquerades as a change.
    private static bool TryBuildDealDriftIncoming(CrmDeal deal, string? currency, out CrmDriftIncoming incoming)
    {
        incoming = null!;
        if (deal.Amount is null || string.IsNullOrWhiteSpace(currency))
            return false;
        try { incoming = new CrmDriftIncoming(deal.Id, Money.Of(deal.Amount.Value, currency), deal.CloseDate); }
        catch (DomainException) { return false; }
        return true;
    }

    // Line-item drift comparable: the line item's own amount, the deal's close date. Amount/date changes of
    // a line item are detected per line. (Quantity-only drift with an unchanged amount is a documented gap.)
    private static bool TryBuildLineDriftIncoming(CrmDeal deal, CrmLineItem li, string? currency, out CrmDriftIncoming incoming)
    {
        incoming = null!;
        if (li.Amount is null || string.IsNullOrWhiteSpace(currency))
            return false;
        try { incoming = new CrmDriftIncoming(deal.Id, Money.Of(li.Amount.Value, currency), deal.CloseDate); }
        catch (DomainException) { return false; }
        return true;
    }
}
