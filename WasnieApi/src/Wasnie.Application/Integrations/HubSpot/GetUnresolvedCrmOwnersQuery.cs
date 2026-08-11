using MediatR;
using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Integrations.Crm;
using Wasnie.Domain.Authorization;
using Wasnie.Domain.Common.Results;
using Wasnie.Domain.Compensation.Enums;

namespace Wasnie.Application.Integrations.HubSpot;

/// <summary>
/// FASE 2d: lists HubSpot owners (that have closed-won deals) which did NOT auto-resolve to a payee — i.e.
/// no existing <see cref="Crm.ICrmOwnerResolver"/> mapping AND no exact email match. These are the owners
/// the user links manually. Read-only.
/// </summary>
public sealed record GetUnresolvedCrmOwnersQuery : IRequest<Result<UnresolvedCrmOwnersDto>>;

public sealed class GetUnresolvedCrmOwnersHandler(
    IApplicationDbContext db,
    ITenantContext tenantContext,
    IAuthorizationService authorizationService,
    IPaidPlanGate paidPlanGate,
    ICrmDealSource dealSource)
    : IRequestHandler<GetUnresolvedCrmOwnersQuery, Result<UnresolvedCrmOwnersDto>>
{
    public async Task<Result<UnresolvedCrmOwnersDto>> Handle(
        GetUnresolvedCrmOwnersQuery request, CancellationToken cancellationToken)
    {
        await authorizationService.RequireAsync(Permission.IntegrationsManage, cancellationToken);
        // Metered capability: the plan is checked after the permission, so an admin on Free is
        // told the truth ("not in your plan") instead of a bare Forbidden. Frozen, not deleted —
        // a downgraded tenant keeps its stored connection and resumes on upgrade.
        await paidPlanGate.RequirePaidPlanAsync("The HubSpot integration", cancellationToken);

        var source = dealSource.SourceName;

        IReadOnlyList<CrmDeal> deals;
        IReadOnlyList<CrmOwner> owners;
        try
        {
            deals = await dealSource.GetClosedWonDealsAsync(tenantContext.TenantId, cancellationToken);
            owners = await dealSource.GetOwnersAsync(tenantContext.TenantId, cancellationToken);
        }
        catch (CrmNotConnectedException ex)
        {
            return Result<UnresolvedCrmOwnersDto>.Failure(ex.Message);
        }
        catch
        {
            return Result<UnresolvedCrmOwnersDto>.Failure("Reading from HubSpot failed. Please try again.");
        }

        var ownerById = owners
            .GroupBy(o => o.Id, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        // Owners that already resolve: an existing mapping, or an exact email match to a payee.
        var mappedOwnerIds = await db.CrmOwnerMappings
            .Where(m => m.Source == source)
            .Select(m => m.CrmOwnerId)
            .ToListAsync(cancellationToken);
        var mapped = new HashSet<string>(mappedOwnerIds, StringComparer.Ordinal);

        var payeeEmails = await db.Payees
            .Where(p => p.Email != null)
            .Select(p => p.Email!)
            .ToListAsync(cancellationToken);
        var payeeEmailSet = new HashSet<string>(payeeEmails, StringComparer.Ordinal);

        // Already-imported CrmSync transactions, to count how many of each owner's deals sit Unassigned.
        var crmTx = await db.CompensationTransactions
            .Where(t => t.Source == TransactionSource.CrmSync && t.ExternalId != null)
            .Select(t => new { t.ExternalId, t.PayeeId })
            .ToListAsync(cancellationToken);
        var unassignedExternalIds = crmTx
            .Where(t => t.PayeeId == null)
            .Select(t => t.ExternalId!)
            .ToHashSet(StringComparer.Ordinal);

        // Group the closed-won deals by owner (deals without an owner are not "owner" rows — they are just
        // Unassigned and handled by the normal assign/reassign UI).
        var dealsByOwner = deals
            .Where(d => !string.IsNullOrEmpty(d.OwnerId))
            .GroupBy(d => d.OwnerId!, StringComparer.Ordinal);

        var unresolved = new List<UnresolvedCrmOwnerDto>();
        foreach (var group in dealsByOwner)
        {
            var ownerId = group.Key;
            if (mapped.Contains(ownerId))
                continue;

            ownerById.TryGetValue(ownerId, out var owner);
            var normalizedEmail = NormalizeEmail(owner?.Email);
            if (normalizedEmail is not null && payeeEmailSet.Contains(normalizedEmail))
                continue; // would auto-resolve by email on the next import — not "unresolved"

            var dealList = group.ToList();
            var unassignedCount = dealList.Count(d => unassignedExternalIds.Contains(d.Id));

            unresolved.Add(new UnresolvedCrmOwnerDto(
                OwnerId: ownerId,
                Name: owner?.DisplayName ?? ownerId,
                Email: owner?.Email,
                Archived: owner?.Archived ?? false,
                ClosedWonDealCount: dealList.Count,
                UnassignedTransactionCount: unassignedCount));
        }

        var ordered = unresolved
            .OrderByDescending(o => o.UnassignedTransactionCount)
            .ThenBy(o => o.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return Result<UnresolvedCrmOwnersDto>.Success(new UnresolvedCrmOwnersDto(ordered.Count, ordered));
    }

    private static string? NormalizeEmail(string? email) =>
        string.IsNullOrWhiteSpace(email) ? null : email.Trim().ToLowerInvariant();
}
