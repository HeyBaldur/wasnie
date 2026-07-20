using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Common.Abstractions;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Domain.Audit;
using Wasnie.Domain.Compensation.Enums;
using Wasnie.Domain.Compensation.Transactions;
using Wasnie.Domain.Exceptions;
using Wasnie.Domain.Integrations.Crm;

namespace Wasnie.Application.Integrations.Crm.Drift;

/// <inheritdoc cref="ICrmDriftPolicy"/>
public sealed class CrmDriftPolicy(IApplicationDbContext db, IGuidGenerator guid) : ICrmDriftPolicy
{
    public async Task<CrmDriftResult> ReconcileAsync(
        TransactionSource newTransactionSource,
        string crmSourceName,
        IReadOnlyList<CrmDriftCandidate> candidates,
        DateTimeOffset now,
        string actor,
        string actorEmail,
        CancellationToken cancellationToken = default)
    {
        if (candidates.Count == 0)
            return CrmDriftResult.Empty;

        var email = string.IsNullOrWhiteSpace(actorEmail) ? actor : actorEmail;

        // Preload existing UNRESOLVED alerts for these transactions in ONE query (no N+1). Keyed by
        // transaction id — the unresolved unique index guarantees at most one per transaction.
        var txIds = candidates.Select(c => c.Existing.Id).ToList();
        var existingAlerts = (await db.CrmDriftAlerts
                .Where(a => a.ResolvedAt == null && txIds.Contains(a.TransactionId))
                .ToListAsync(cancellationToken))
            .ToDictionary(a => a.TransactionId);

        var outcomes = new List<CrmDriftOutcome>(candidates.Count);

        // Pending auto-voids are deferred to a second save: we void (+alert/audit) and SAVE first so the
        // old rows are Cancelled in the DB, THEN insert the re-created rows. This keeps the filtered unique
        // index (Status <> Cancelled) happy regardless of EF's insert/update ordering within one batch.
        var recreations = new List<PendingRecreation>();
        var anyPhaseAChanges = false;

        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var tx = candidate.Existing;
            var incoming = candidate.Incoming;

            var amountChanged = !MoneyEquals(tx.Amount.Amount, tx.Amount.Currency,
                incoming.Amount.Amount, incoming.Amount.Currency);
            // A missing close date never counts as drift — the importer substitutes "today" for missing
            // dates, which must not masquerade as a change.
            var dateChanged = incoming.CloseDate.HasValue && tx.TransactionDate != incoming.CloseDate.Value;

            if (!amountChanged && !dateChanged)
            {
                outcomes.Add(new CrmDriftOutcome(incoming.ExternalDealId, tx.Id, CrmDriftAction.NoDrift, null));
                continue;
            }

            var newCloseDate = incoming.CloseDate ?? tx.TransactionDate;

            // Blindaje (Rule 10): auto-void ONLY a strictly Pending transaction. Anything else → alert.
            if (tx.Status == CompensationTransactionStatus.Pending)
            {
                var reason = BuildAutoVoidReason(tx, incoming, amountChanged, dateChanged, newCloseDate);
                try
                {
                    // Re-check + Cancel() both enforce "Pending only"; if a race moved it to Calculated,
                    // Cancel throws and we fall through to the alert path (degrade, never auto-void).
                    if (tx.Status != CompensationTransactionStatus.Pending)
                        throw new DomainException("Transaction is no longer Pending.");
                    tx.Cancel(reason, actor, now, guid.NewGuid());
                }
                catch (DomainException)
                {
                    RecordAlert(existingAlerts, crmSourceName, tx, incoming, amountChanged, dateChanged,
                        newCloseDate, now, actor, email);
                    anyPhaseAChanges = true;
                    outcomes.Add(new CrmDriftOutcome(
                        incoming.ExternalDealId, tx.Id, CrmDriftAction.AlertedRaceDegraded, null));
                    continue;
                }

                var newId = guid.NewGuid();
                recreations.Add(new PendingRecreation(tx, incoming, newCloseDate, newId, reason));

                // Audit the void now (the create is audited in phase B once the row exists).
                db.AuditLogs.Add(AuditLog.Create(
                    tenantId: tx.TenantId,
                    timestampUtc: now.UtcDateTime,
                    actorUserId: actor,
                    actorEmail: email,
                    action: AuditActions.CrmDriftAutoResolved,
                    resourceType: ResourceTypes.Transaction,
                    resourceId: tx.Id.ToString(),
                    resourceDisplayName: tx.ReferenceNumber,
                    beforeJson: JsonSerializer.Serialize(new
                    {
                        status = nameof(CompensationTransactionStatus.Pending),
                        amount = tx.Amount.Amount,
                        currency = tx.Amount.Currency,
                        transactionDate = tx.TransactionDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    }),
                    afterJson: JsonSerializer.Serialize(new
                    {
                        status = nameof(CompensationTransactionStatus.Cancelled),
                        reason,
                        supersededByTransactionId = newId,
                    })));

                anyPhaseAChanges = true;
                outcomes.Add(new CrmDriftOutcome(
                    incoming.ExternalDealId, tx.Id, CrmDriftAction.AutoVoidedAndRecreated, newId));
            }
            else
            {
                RecordAlert(existingAlerts, crmSourceName, tx, incoming, amountChanged, dateChanged,
                    newCloseDate, now, actor, email);
                anyPhaseAChanges = true;
                outcomes.Add(new CrmDriftOutcome(incoming.ExternalDealId, tx.Id, CrmDriftAction.Alerted, null));
            }
        }

        // Phase A — persist voids, alerts and their audits so the old rows are Cancelled before any insert.
        if (anyPhaseAChanges)
            await db.SaveChangesAsync(cancellationToken);

        // Phase B — create the replacement transactions (Opción B): same payee, same reference/externalId,
        // the deal's CURRENT amount and date. The voided originals remain as history.
        foreach (var r in recreations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var old = r.Old;

            var newTx = CompensationTransaction.Ingest(
                tenantId: old.TenantId,
                referenceNumber: old.ReferenceNumber,
                payeeId: old.PayeeId,
                amount: r.Incoming.Amount,
                transactionDate: r.NewCloseDate,
                source: newTransactionSource,
                ingestedBy: actor,
                id: r.NewId,
                now: now,
                eventId: guid.NewGuid(),
                externalId: old.ExternalId,
                quantity: old.Quantity);

            db.CompensationTransactions.Add(newTx);

            db.AuditLogs.Add(AuditLog.Create(
                tenantId: newTx.TenantId,
                timestampUtc: now.UtcDateTime,
                actorUserId: actor,
                actorEmail: email,
                action: AuditActions.CrmDriftAutoResolved,
                resourceType: ResourceTypes.Transaction,
                resourceId: newTx.Id.ToString(),
                resourceDisplayName: newTx.ReferenceNumber,
                beforeJson: JsonSerializer.Serialize(new
                {
                    supersedesTransactionId = old.Id,
                    amount = old.Amount.Amount,
                    currency = old.Amount.Currency,
                    transactionDate = old.TransactionDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                }),
                afterJson: JsonSerializer.Serialize(new
                {
                    amount = newTx.Amount.Amount,
                    currency = newTx.Amount.Currency,
                    transactionDate = newTx.TransactionDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                })));
        }

        if (recreations.Count > 0)
            await db.SaveChangesAsync(cancellationToken);

        return new CrmDriftResult(outcomes);
    }

    private void RecordAlert(
        IDictionary<Guid, CrmDriftAlert> existing,
        string crmSourceName,
        CompensationTransaction tx,
        CrmDriftIncoming incoming,
        bool amountChanged,
        bool dateChanged,
        DateOnly newCloseDate,
        DateTimeOffset now,
        string actor,
        string actorEmail)
    {
        if (existing.TryGetValue(tx.Id, out var alert))
        {
            // Same deal still/again drifts → refresh the latest CRM values (old values never change).
            alert.Refresh(amountChanged, incoming.Amount.Amount, incoming.Amount.Currency,
                dateChanged, newCloseDate, now, actor);
        }
        else
        {
            alert = CrmDriftAlert.Create(
                id: guid.NewGuid(),
                tenantId: tx.TenantId,
                source: crmSourceName,
                externalDealId: incoming.ExternalDealId,
                transactionId: tx.Id,
                referenceNumber: tx.ReferenceNumber,
                transactionStatus: tx.Status,
                amountChanged: amountChanged,
                oldAmount: tx.Amount.Amount,
                oldCurrency: tx.Amount.Currency,
                newAmount: incoming.Amount.Amount,
                newCurrency: incoming.Amount.Currency,
                dateChanged: dateChanged,
                oldCloseDate: tx.TransactionDate,
                newCloseDate: newCloseDate,
                detectedAt: now,
                detectedBy: actor);
            db.CrmDriftAlerts.Add(alert);
            existing[tx.Id] = alert;
        }

        db.AuditLogs.Add(AuditLog.Create(
            tenantId: tx.TenantId,
            timestampUtc: now.UtcDateTime,
            actorUserId: actor,
            actorEmail: actorEmail,
            action: AuditActions.CrmDriftDetected,
            resourceType: ResourceTypes.Transaction,
            resourceId: tx.Id.ToString(),
            resourceDisplayName: tx.ReferenceNumber,
            beforeJson: JsonSerializer.Serialize(new
            {
                status = tx.Status.ToString(),
                amount = tx.Amount.Amount,
                currency = tx.Amount.Currency,
                transactionDate = tx.TransactionDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            }),
            afterJson: JsonSerializer.Serialize(new
            {
                newAmount = incoming.Amount.Amount,
                newCurrency = incoming.Amount.Currency,
                newCloseDate = newCloseDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                amountChanged,
                dateChanged,
            })));
    }

    // Both sides have been normalized through Money.Of (4-dp banker's rounding), so a plain decimal compare
    // here will NOT false-positive on formatting/precision differences. Currency compared case-insensitively.
    private static bool MoneyEquals(decimal aAmount, string aCurrency, decimal bAmount, string bCurrency) =>
        aAmount == bAmount && string.Equals(aCurrency, bCurrency, StringComparison.OrdinalIgnoreCase);

    private static string BuildAutoVoidReason(
        CompensationTransaction tx,
        CrmDriftIncoming incoming,
        bool amountChanged,
        bool dateChanged,
        DateOnly newCloseDate)
    {
        var sb = new StringBuilder("Auto-voided: deal updated in HubSpot — ");
        var parts = new List<string>(2);
        if (amountChanged)
            parts.Add(string.Format(CultureInfo.InvariantCulture,
                "amount {0} {1}→{2} {3}",
                tx.Amount.Amount, tx.Amount.Currency, incoming.Amount.Amount, incoming.Amount.Currency));
        if (dateChanged)
            parts.Add(string.Format(CultureInfo.InvariantCulture,
                "close date {0}→{1}",
                tx.TransactionDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                newCloseDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)));
        sb.Append(string.Join("; ", parts));
        return sb.ToString();
    }

    private sealed record PendingRecreation(
        CompensationTransaction Old,
        CrmDriftIncoming Incoming,
        DateOnly NewCloseDate,
        Guid NewId,
        string Reason);
}
