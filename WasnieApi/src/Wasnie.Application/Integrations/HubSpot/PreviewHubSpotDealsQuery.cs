using MediatR;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Integrations.Crm;
using Wasnie.Domain.Authorization;
using Wasnie.Domain.Common.Results;

namespace Wasnie.Application.Integrations.HubSpot;

/// <summary>
/// FASE 2a verification: lists the closed-won deals read from the connected HubSpot account, joined with
/// owner name/email. READ-ONLY — it creates NO transactions and writes nothing to HubSpot. Lets the owner
/// confirm on screen that the deal read + closed-won filter work before any materialization (Fase 2c).
/// </summary>
public sealed record PreviewHubSpotDealsQuery : IRequest<Result<HubSpotDealsPreviewDto>>;

public sealed class PreviewHubSpotDealsHandler(
    ITenantContext tenantContext,
    IAuthorizationService authorizationService,
    ICrmDealSource dealSource)
    : IRequestHandler<PreviewHubSpotDealsQuery, Result<HubSpotDealsPreviewDto>>
{
    public async Task<Result<HubSpotDealsPreviewDto>> Handle(
        PreviewHubSpotDealsQuery request, CancellationToken cancellationToken)
    {
        await authorizationService.RequireAsync(Permission.IntegrationsManage, cancellationToken);

        try
        {
            var deals = await dealSource.GetClosedWonDealsAsync(tenantContext.TenantId, cancellationToken);
            var owners = await dealSource.GetOwnersAsync(tenantContext.TenantId, cancellationToken);
            var ownersById = owners
                .GroupBy(o => o.Id, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

            var items = deals.Select(d =>
            {
                CrmOwner? owner = d.OwnerId is not null && ownersById.TryGetValue(d.OwnerId, out var o) ? o : null;
                return new HubSpotDealPreviewItemDto(
                    d.Id, d.Name, d.Amount, d.CurrencyCode, d.CloseDate, d.OwnerId,
                    owner?.DisplayName, owner?.Email);
            }).ToList();

            return Result<HubSpotDealsPreviewDto>.Success(new HubSpotDealsPreviewDto(items.Count, items));
        }
        catch (CrmNotConnectedException ex)
        {
            return Result<HubSpotDealsPreviewDto>.Failure(ex.Message);
        }
        catch
        {
            return Result<HubSpotDealsPreviewDto>.Failure("Reading deals from HubSpot failed. Please try again.");
        }
    }
}
