using MediatR;
using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Common.Abstractions;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Integrations.Crm;
using Wasnie.Domain.Audit;
using Wasnie.Domain.Authorization;
using Wasnie.Domain.Common.Results;
using Wasnie.Domain.Compensation.Enums;
using Wasnie.Domain.Compensation.Transactions;
using Wasnie.Domain.Compensation.ValueObjects;
using Wasnie.Domain.Exceptions;

namespace Wasnie.Application.Integrations.HubSpot;

/// <summary>
/// FASE 2c: materialises the connected CRM's closed-won deals as Wasnie transactions, idempotently.
///
/// READ-ONLY against the CRM. For each closed-won deal it does LOOKUP-BEFORE-CREATE keyed by the deal id
/// (stored as <see cref="CompensationTransaction.ExternalId"/> with Source = CrmSync, guarded by the unique
/// (TenantId, Source, ExternalId) index): already-imported deals are skipped, so re-running never
/// duplicates. New deals become Pending transactions assigned to the resolved payee (FASE 2b) or left
/// Unassigned. From there the EXISTING engine takes over — nothing here recalculates or touches
/// already-paid transactions (Rule 10).
///
/// Money-critical: the audit entry and the created transactions commit atomically (AuditBehavior).
/// </summary>
public sealed record ImportHubSpotDealsCommand
    : IRequest<Result<HubSpotImportResultDto>>, IMoneyCriticalCommand
{
    public string AuditAction => AuditActions.CrmDealsImported;
    public string AuditResourceType => ResourceTypes.Integration;
    public string? AuditResourceId { get; set; } = "HubSpot";
    public string? AuditDisplayName => "HubSpot closed-won deals imported";
}

public sealed class ImportHubSpotDealsHandler(
    IApplicationDbContext db,
    ITenantContext tenantContext,
    ICurrentUserService currentUser,
    IClock clock,
    IGuidGenerator guid,
    IAuthorizationService authorizationService,
    ICrmDealSource dealSource,
    ICrmOwnerResolver ownerResolver)
    : IRequestHandler<ImportHubSpotDealsCommand, Result<HubSpotImportResultDto>>
{
    public async Task<Result<HubSpotImportResultDto>> Handle(
        ImportHubSpotDealsCommand request, CancellationToken cancellationToken)
    {
        await authorizationService.RequireAsync(Permission.IntegrationsManage, cancellationToken);

        var tenantId = tenantContext.TenantId;
        var source = dealSource.SourceName;

        IReadOnlyList<CrmDeal> deals;
        IReadOnlyList<CrmOwner> owners;
        string? defaultCurrency;
        try
        {
            deals = await dealSource.GetClosedWonDealsAsync(tenantId, cancellationToken);
            owners = await dealSource.GetOwnersAsync(tenantId, cancellationToken);
            defaultCurrency = await dealSource.GetDefaultCurrencyAsync(tenantId, cancellationToken);
        }
        catch (CrmNotConnectedException ex)
        {
            return Result<HubSpotImportResultDto>.Failure(ex.Message);
        }
        catch
        {
            return Result<HubSpotImportResultDto>.Failure("Reading from HubSpot failed. Please try again.");
        }

        var ownerEmailById = owners
            .GroupBy(o => o.Id, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().Email, StringComparer.Ordinal);

        // Lookup-before-create: ids of deals already imported for this tenant (idempotency).
        var existingExternalIds = await db.CompensationTransactions
            .Where(t => t.Source == TransactionSource.CrmSync && t.ExternalId != null)
            .Select(t => t.ExternalId!)
            .ToListAsync(cancellationToken);
        var seen = new HashSet<string>(existingExternalIds, StringComparer.Ordinal);

        var now = clock.UtcNowOffset;
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var actor = currentUser.UserId ?? "system";

        int created = 0, assigned = 0, unassigned = 0, skippedExisting = 0, skippedInvalid = 0, newMappings = 0;
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
                resolution = await ownerResolver.ResolveAsync(tenantId, source, ownerId, email, cancellationToken);
                ownerResolutionCache[ownerId] = resolution;
                if (resolution.NewMappingCreated)
                    newMappings++;
            }
            return resolution.PayeeId;
        }

        foreach (var deal in deals)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(deal.Id) || !seen.Add(deal.Id))
            {
                // Blank id (shouldn't happen) or already imported → skip (idempotent).
                if (!string.IsNullOrWhiteSpace(deal.Id))
                    skippedExisting++;
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

        var warnings = new List<string>();
        if (missingAmount > 0) warnings.Add($"{missingAmount} deal(s) skipped: no amount.");
        if (missingCurrency > 0) warnings.Add($"{missingCurrency} deal(s) skipped: no currency (and no account default).");
        if (missingCloseDate > 0) warnings.Add($"{missingCloseDate} deal(s) had no close date — used today's date.");

        return Result<HubSpotImportResultDto>.Success(new HubSpotImportResultDto(
            DealsRead: deals.Count,
            Created: created,
            AssignedToPayee: assigned,
            Unassigned: unassigned,
            SkippedAlreadyImported: skippedExisting,
            SkippedInvalid: skippedInvalid,
            NewOwnerMappings: newMappings,
            Warnings: warnings));
    }
}
