using MediatR;
using Wasnie.Application.Common.Abstractions;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Integrations.Crm;
using Wasnie.Domain.Audit;
using Wasnie.Domain.Authorization;
using Wasnie.Domain.Common.Results;
using Wasnie.Domain.Compensation.Transactions;

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
    ITenantContext tenantContext,
    ICurrentUserService currentUser,
    IClock clock,
    IAuthorizationService authorizationService,
    ICrmDealSource dealSource,
    ICrmDealReconciler reconciler)
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
            // FULL backfill: every closed-won deal. (The Phase-3 polling job uses the incremental variant.)
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

        var actor = currentUser.UserId ?? "system";
        var result = await reconciler.ReconcileAsync(
            tenantId, source, deals, owners, defaultCurrency,
            actor, currentUser.Email ?? actor, clock.UtcNowOffset, cancellationToken);

        var warnings = new List<string>();
        if (result.MissingAmount > 0) warnings.Add($"{result.MissingAmount} deal(s) skipped: no amount.");
        if (result.MissingCurrency > 0) warnings.Add($"{result.MissingCurrency} deal(s) skipped: no currency (and no account default).");
        if (result.MissingCloseDate > 0) warnings.Add($"{result.MissingCloseDate} deal(s) had no close date — used today's date.");
        if (result.SkippedBlocked > 0) warnings.Add($"{result.SkippedBlocked} deal(s) skipped: a prior voided transaction had already been processed — cannot re-import.");
        if (result.DriftAutoResolved > 0) warnings.Add($"{result.DriftAutoResolved} deal(s) changed in HubSpot and were updated automatically (the pending transaction was re-created with the new values).");
        if (result.DriftAlertsRaised > 0) warnings.Add($"{result.DriftAlertsRaised} deal(s) changed in HubSpot after their commission was already calculated or paid — review the “needs attention” card.");

        return Result<HubSpotImportResultDto>.Success(new HubSpotImportResultDto(
            DealsRead: result.DealsRead,
            Created: result.Created,
            AssignedToPayee: result.AssignedToPayee,
            Unassigned: result.Unassigned,
            SkippedAlreadyImported: result.SkippedAlreadyImported,
            SkippedInvalid: result.SkippedInvalid,
            NewOwnerMappings: result.NewOwnerMappings,
            DriftAutoResolved: result.DriftAutoResolved,
            DriftAlertsRaised: result.DriftAlertsRaised,
            Warnings: warnings));
    }
}
