using MediatR;
using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Compensation.Common;
using Wasnie.Application.Compensation.DTOs;
using Wasnie.Application.Compensation.Queries.Transactions;
using Wasnie.Domain.Authorization;
using Wasnie.Domain.Common.Results;

namespace Wasnie.Application.Compensation.Handlers.Transactions;

public sealed class GetPlanOptionsForTransactionHandler(
    IApplicationDbContext db,
    ITenantContext tenantContext,
    IAuthorizationService authorizationService)
    : IRequestHandler<GetPlanOptionsForTransactionQuery, Result<PlanOptionsDto>>
{
    public async Task<Result<PlanOptionsDto>> Handle(
        GetPlanOptionsForTransactionQuery request, CancellationToken cancellationToken)
    {
        // Reading this is part of creating a transaction, so it is gated on the same permission.
        await authorizationService.RequireAsync(Permission.TransactionsCreate, cancellationToken);

        var candidates = await PayeePlanCandidates.LoadAsync(
            db, tenantContext.TenantId, request.PayeeId,
            request.TransactionDate, request.Currency, cancellationToken);

        if (candidates.Count == 0)
            return Result<PlanOptionsDto>.Success(new PlanOptionsDto([], SelectionRequired: false));

        var planIds = candidates.Select(c => c.PlanId).Distinct().ToList();
        var plans = await db.CompensationPlans
            .IgnoreQueryFilters()
            .Where(p => p.TenantId == tenantContext.TenantId && planIds.Contains(p.Id))
            .Select(p => new { p.Id, p.Name, p.Currency })
            .ToDictionaryAsync(p => p.Id, cancellationToken);

        var options = candidates
            .Select(c =>
            {
                var plan = plans.GetValueOrDefault(c.PlanId);
                return new PlanOptionDto(
                    PlanAssignmentId: c.Id,
                    PlanId: c.PlanId,
                    PlanName: plan?.Name ?? string.Empty,
                    PlanCurrency: plan?.Currency ?? request.Currency,
                    EffectiveStart: c.EffectivePeriod!.Start,
                    EffectiveEnd: c.EffectivePeriod.End);
            })
            .OrderBy(o => o.PlanName)
            .ThenBy(o => o.EffectiveStart)
            .ToList();

        // Ambiguity — and therefore the obligation to choose — is decided here, not in the browser.
        return Result<PlanOptionsDto>.Success(
            new PlanOptionsDto(options, SelectionRequired: options.Count >= 2));
    }
}
