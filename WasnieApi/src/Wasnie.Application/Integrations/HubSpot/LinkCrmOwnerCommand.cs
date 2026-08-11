using MediatR;
using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Common.Abstractions;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Integrations.Crm;
using Wasnie.Domain.Audit;
using Wasnie.Domain.Authorization;
using Wasnie.Domain.Common.Results;
using Wasnie.Domain.Compensation.Enums;
using Wasnie.Domain.Exceptions;
using Wasnie.Domain.Integrations.Crm;

namespace Wasnie.Application.Integrations.HubSpot;

/// <summary>
/// FASE 2d: manually links a HubSpot owner to an existing Wasnie payee (never creates a payee). Creates a
/// <see cref="CrmOwnerMapping"/> so future deals from that owner auto-assign.
///
/// Retroactive policy (reported decision): when <see cref="ReassignExistingUnassigned"/> is true, the
/// owner's already-imported transactions that are STILL Unassigned and NOT yet paid are reassigned to the
/// payee. Paid/assigned transactions are NEVER touched (Rule 10 — anti-double-pay / immutability).
///
/// Money-critical: the mapping, the reassignments and the audit entry commit atomically.
/// </summary>
public sealed record LinkCrmOwnerCommand(
    string OwnerId,
    Guid PayeeId,
    bool ReassignExistingUnassigned)
    : IRequest<Result<LinkCrmOwnerResultDto>>, IMoneyCriticalCommand
{
    public string AuditAction => AuditActions.CrmOwnerLinked;
    public string AuditResourceType => ResourceTypes.Integration;
    public string? AuditResourceId { get; set; }
    public string? AuditDisplayName => $"Linked HubSpot owner {OwnerId} → payee {PayeeId}";
}

public sealed class LinkCrmOwnerHandler(
    IApplicationDbContext db,
    ITenantContext tenantContext,
    ICurrentUserService currentUser,
    IClock clock,
    IGuidGenerator guid,
    IAuthorizationService authorizationService,
    IPaidPlanGate paidPlanGate,
    ICrmDealSource dealSource)
    : IRequestHandler<LinkCrmOwnerCommand, Result<LinkCrmOwnerResultDto>>
{
    public async Task<Result<LinkCrmOwnerResultDto>> Handle(
        LinkCrmOwnerCommand request, CancellationToken cancellationToken)
    {
        await authorizationService.RequireAsync(Permission.IntegrationsManage, cancellationToken);
        // Metered capability: the plan is checked after the permission, so an admin on Free is
        // told the truth ("not in your plan") instead of a bare Forbidden. Frozen, not deleted —
        // a downgraded tenant keeps its stored connection and resumes on upgrade.
        await paidPlanGate.RequirePaidPlanAsync("The HubSpot integration", cancellationToken);

        if (string.IsNullOrWhiteSpace(request.OwnerId))
            return Result<LinkCrmOwnerResultDto>.Failure("Owner id is required.");

        var tenantId = tenantContext.TenantId;
        var source = dealSource.SourceName;

        var payee = await db.Payees.FirstOrDefaultAsync(p => p.Id == request.PayeeId, cancellationToken);
        if (payee is null)
            return Result<LinkCrmOwnerResultDto>.Failure("Payee not found.");

        var existing = await db.CrmOwnerMappings
            .FirstOrDefaultAsync(m => m.Source == source && m.CrmOwnerId == request.OwnerId, cancellationToken);
        if (existing is not null)
            return Result<LinkCrmOwnerResultDto>.Failure(
                "This owner is already linked to a payee. Edit the existing mapping instead.");

        var now = clock.UtcNowOffset;
        var actor = currentUser.UserId ?? "system";

        var mapping = CrmOwnerMapping.Create(
            id: guid.NewGuid(),
            tenantId: tenantId,
            source: source,
            crmOwnerId: request.OwnerId,
            payeeId: request.PayeeId,
            matchMethod: CrmOwnerMatchMethod.Manual,
            createdBy: actor,
            now: now);
        db.CrmOwnerMappings.Add(mapping);
        request.AuditResourceId = mapping.Id.ToString();

        var reassigned = 0;
        if (request.ReassignExistingUnassigned)
            reassigned = await ReassignUnassignedAsync(tenantId, request.OwnerId, request.PayeeId, now, actor, cancellationToken);

        await db.SaveChangesAsync(cancellationToken);

        return Result<LinkCrmOwnerResultDto>.Success(new LinkCrmOwnerResultDto(reassigned));
    }

    /// <summary>
    /// Re-reads the owner's closed-won deals (source of truth for which deals are theirs) and reassigns the
    /// matching transactions that are still Unassigned. Paid transactions are excluded by the PayeeId-null
    /// filter and additionally guarded by <c>Assign</c> (which rejects Paid).
    /// </summary>
    private async Task<int> ReassignUnassignedAsync(
        Guid tenantId, string ownerId, Guid payeeId, DateTimeOffset now, string actor, CancellationToken cancellationToken)
    {
        IReadOnlyList<CrmDeal> deals;
        try
        {
            deals = await dealSource.GetClosedWonDealsAsync(tenantId, cancellationToken);
        }
        catch (CrmNotConnectedException)
        {
            return 0; // mapping still created; future imports will assign — just skip retroactive cleanup
        }

        var ownerDealIds = deals
            .Where(d => string.Equals(d.OwnerId, ownerId, StringComparison.Ordinal) && !string.IsNullOrEmpty(d.Id))
            .Select(d => d.Id)
            .ToHashSet(StringComparer.Ordinal);
        if (ownerDealIds.Count == 0)
            return 0;

        var candidates = await db.CompensationTransactions
            .Where(t => t.Source == TransactionSource.CrmSync
                        && t.PayeeId == null
                        && t.ExternalId != null
                        && ownerDealIds.Contains(t.ExternalId))
            .ToListAsync(cancellationToken);

        var reassigned = 0;
        foreach (var tx in candidates)
        {
            try
            {
                tx.Assign(payeeId, "Linked via CRM owner mapping queue", actor, now, guid.NewGuid());
                reassigned++;
            }
            catch (DomainException)
            {
                // Defensive: skip anything the domain refuses (e.g. already Paid). Never force-pay.
            }
        }

        return reassigned;
    }
}
