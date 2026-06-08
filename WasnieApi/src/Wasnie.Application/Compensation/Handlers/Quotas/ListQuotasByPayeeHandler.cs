using MediatR;
using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Common.Abstractions;
using Wasnie.Application.Common.Helpers;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Common.Models;
using Wasnie.Application.Compensation.DTOs;
using Wasnie.Application.Compensation.Queries.Quotas;
using Wasnie.Domain.Authorization;
using Wasnie.Domain.Common.Results;
using Wasnie.Domain.Compensation.Enums;

namespace Wasnie.Application.Compensation.Handlers.Quotas;

public sealed class ListQuotasByPayeeHandler(IApplicationDbContext db, IAuthorizationService authorizationService, IClock clock)
    : IRequestHandler<ListQuotasByPayeeQuery, Result<PagedResult<QuotaSummaryDto>>>
{
    public async Task<Result<PagedResult<QuotaSummaryDto>>> Handle(ListQuotasByPayeeQuery request, CancellationToken cancellationToken)
    {
        await authorizationService.RequireAsync(Permission.QuotasRead, cancellationToken);
        var p = request.Pagination;

        // Load all quotas for this payee in one go — typically few per payee.
        // DateOnly comparisons on owned DateRange (Period.Start/End) don't translate in SQL,
        // so period filtering is applied in-memory (same pattern as CreditAllocationService).
        var allQuotas = await db.Quotas
            .Where(q => q.PayeeId == request.PayeeId)
            .OrderByDescending(q => q.Period.Start)
            .ToListAsync(cancellationToken);

        // Apply status filter
        IEnumerable<Wasnie.Domain.Compensation.Quotas.Quota> filtered = allQuotas;
        if (!string.IsNullOrWhiteSpace(p.Status) &&
            Enum.TryParse<QuotaStatus>(p.Status, ignoreCase: true, out var status))
            filtered = filtered.Where(q => q.Status == status);

        // Apply period intersection filter — quota period [Start, End] must intersect [from, to]
        var today = DateOnly.FromDateTime(clock.UtcNow);
        var (from, to) = PeriodHelper.ComputeDateRange(p.Period, today);
        if (from.HasValue || to.HasValue)
            filtered = filtered.Where(q =>
                (!from.HasValue || q.Period.End >= from.Value) &&
                (!to.HasValue || q.Period.Start <= to.Value));

        var filteredList = filtered.ToList();
        var totalCount = filteredList.Count;
        var pageItems = filteredList
            .Skip((p.Page - 1) * p.PageSize)
            .Take(p.PageSize)
            .ToList();

        // Load plan names + currencies for the page — both needed for validity check
        var planIds = pageItems.Select(q => q.PlanId).Distinct().ToList();
        var planInfoById = planIds.Count > 0
            ? await db.CompensationPlans
                .Where(pl => planIds.Contains(pl.Id))
                .Select(pl => new { pl.Id, pl.Name, pl.Currency })
                .ToDictionaryAsync(pl => pl.Id, pl => (pl.Name, pl.Currency), cancellationToken)
            : new Dictionary<Guid, (string Name, string Currency)>();

        var payee = await db.Payees.FirstOrDefaultAsync(x => x.Id == request.PayeeId, cancellationToken);

        var dtos = pageItems.Select(q =>
        {
            var planInfo = planInfoById.GetValueOrDefault(q.PlanId, (Name: string.Empty, Currency: string.Empty));
            var isCurrencyValid = string.Equals(q.Amount.Currency, planInfo.Currency, StringComparison.OrdinalIgnoreCase);
            return new QuotaSummaryDto(
                q.Id, q.TenantId, q.PayeeId,
                payee?.FullName ?? string.Empty,
                payee?.EmployeeCode ?? string.Empty,
                q.PlanId,
                planInfo.Name,
                q.MeasurementType, q.Amount.Amount, q.Amount.Currency,
                q.Period.Start, q.Period.End, q.Status.ToString(), q.Notes, q.CreatedAt,
                IsCurrencyValid: isCurrencyValid,
                PlanCurrency: planInfo.Currency);
        }).ToList();

        return Result<PagedResult<QuotaSummaryDto>>.Success(new PagedResult<QuotaSummaryDto>
        {
            Items = dtos,
            TotalCount = totalCount,
            Page = p.Page,
            PageSize = p.PageSize,
        });
    }
}
