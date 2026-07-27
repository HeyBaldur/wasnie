using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Common.Abstractions;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Domain.Audit;
using Wasnie.Domain.Compensation.Enums;
using Wasnie.Domain.Integrations.Crm;

namespace Wasnie.Application.Integrations.Crm;

/// <inheritdoc cref="IDealLostReconciler"/>
public sealed class DealLostReconciler(
    IApplicationDbContext db,
    ICrmDealSource dealSource,
    IGuidGenerator guid)
    : IDealLostReconciler
{
    /// <summary>The deal id is the part of the ExternalId before the first '-' (deal-level = "{dealId}",
    /// line-level = "{dealId}-{lineId}"). HubSpot ids are numeric, so this split is unambiguous.</summary>
    private static string DealIdOf(string externalId)
    {
        var dash = externalId.IndexOf('-');
        return dash < 0 ? externalId : externalId[..dash];
    }

    public async Task<int> ReconcileAsync(
        Guid tenantId,
        string sourceName,
        string actor,
        string actorEmail,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        var email = string.IsNullOrWhiteSpace(actorEmail) ? actor : actorEmail;

        // The commissions at risk: CrmSync transactions already Calculated or Paid (Pending are auto-handled
        // by the forward drift path; Cancelled are already gone). These are the only ones a lost deal harms.
        var atRisk = await db.CompensationTransactions
            .Where(t => t.Source == TransactionSource.CrmSync
                     && (t.Status == CompensationTransactionStatus.Calculated
                      || t.Status == CompensationTransactionStatus.Paid)
                     && t.ExternalId != null)
            .Select(t => new { t.Id, t.ExternalId, t.ReferenceNumber, t.Status })
            .ToListAsync(cancellationToken);

        if (atRisk.Count == 0)
            return 0;

        var dealIds = atRisk.Select(t => DealIdOf(t.ExternalId!)).Distinct(StringComparer.Ordinal).ToList();

        // Ask the CRM for the CURRENT won-status by id (no closed-won filter). Null-safe against a mock/stub.
        var statuses = await dealSource.GetDealStatusesByIdsAsync(tenantId, dealIds, cancellationToken)
                       ?? [];
        var wonById = statuses.ToDictionary(s => s.Id, s => s.IsClosedWon, StringComparer.Ordinal);

        // A deal is "lost" ONLY when the CRM explicitly returned it as not-won. A deal absent from the batch
        // (deleted / archived / no access / transient error) is left alone — we never destroy a commission on
        // an ambiguous absence.
        var lostTxs = atRisk
            .Where(t => wonById.TryGetValue(DealIdOf(t.ExternalId!), out var won) && !won)
            .ToList();

        if (lostTxs.Count == 0)
            return 0;

        var lostTxIds = lostTxs.Select(t => t.Id).ToList();

        // Live commission per transaction (sum of non-superseded credited amounts), one query. Shown in the
        // alert so the admin sees exactly how much a revert would take back. Currency is the credit's.
        var creditRows = await db.Credits
            .Where(c => c.SupersededAt == null && lostTxIds.Contains(c.TransactionId))
            .Select(c => new { c.TransactionId, Amount = c.CreditedAmount.Amount, Currency = c.CreditedAmount.Currency })
            .ToListAsync(cancellationToken);
        var commissionByTx = creditRows
            .GroupBy(c => c.TransactionId)
            .ToDictionary(
                g => g.Key,
                g => (Amount: g.Sum(x => x.Amount), Currency: g.First().Currency));

        // Preload existing UNRESOLVED alerts for these transactions (one query, ≤1 per tx by the index).
        var existing = (await db.DealLostAlerts
                .Where(a => a.ResolvedAt == null && lostTxIds.Contains(a.TransactionId))
                .ToListAsync(cancellationToken))
            .ToDictionary(a => a.TransactionId);

        foreach (var t in lostTxs)
        {
            var (amount, currency) = commissionByTx.TryGetValue(t.Id, out var c)
                ? c
                : (0m, "");
            var dealId = DealIdOf(t.ExternalId!);

            if (existing.TryGetValue(t.Id, out var alert))
            {
                alert.Refresh(t.Status, amount, currency, now, actor);
            }
            else
            {
                alert = DealLostAlert.Create(
                    id: guid.NewGuid(),
                    tenantId: tenantId,
                    source: sourceName,
                    externalDealId: dealId,
                    transactionId: t.Id,
                    referenceNumber: t.ReferenceNumber,
                    transactionStatus: t.Status,
                    commissionAmount: amount,
                    commissionCurrency: currency,
                    detectedAt: now,
                    detectedBy: actor);
                db.DealLostAlerts.Add(alert);
                existing[t.Id] = alert;
            }

            db.AuditLogs.Add(AuditLog.Create(
                tenantId: tenantId,
                timestampUtc: now.UtcDateTime,
                actorUserId: actor,
                actorEmail: email,
                action: AuditActions.DealLostDetected,
                resourceType: ResourceTypes.Transaction,
                resourceId: t.Id.ToString(),
                resourceDisplayName: t.ReferenceNumber,
                beforeJson: null,
                afterJson: JsonSerializer.Serialize(new
                {
                    externalDealId = dealId,
                    transactionStatus = t.Status.ToString(),
                    commissionAmount = amount,
                    commissionCurrency = currency,
                })));
        }

        await db.SaveChangesAsync(cancellationToken);
        return lostTxs.Count;
    }
}
