using MediatR;
using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Common.Abstractions;
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

        // Apply period filter: "active" = PeriodEnd >= today (current or future quotas)
        var today = DateOnly.FromDateTime(clock.UtcNow);
        if (string.Equals(p.Period, "active", StringComparison.OrdinalIgnoreCase))
            filtered = filtered.Where(q => q.Period.End >= today);

        var filteredList = filtered.ToList();
        var totalCount = filteredList.Count;
        var pageItems = filteredList
            .Skip((p.Page - 1) * p.PageSize)
            .Take(p.PageSize)
            .ToList();

        // Load plan names for the page
        var planIds = pageItems.Select(q => q.PlanId).Distinct().ToList();
        var planNameById = planIds.Count > 0
            ? await db.CompensationPlans
                .Where(pl => planIds.Contains(pl.Id))
                .Select(pl => new { pl.Id, pl.Name })
                .ToDictionaryAsync(pl => pl.Id, pl => pl.Name, cancellationToken)
            : new Dictionary<Guid, string>();

        var payee = await db.Payees.FirstOrDefaultAsync(x => x.Id == request.PayeeId, cancellationToken);

        var dtos = pageItems.Select(q => new QuotaSummaryDto(
            q.Id, q.TenantId, q.PayeeId,
            payee?.FullName ?? string.Empty,
            payee?.EmployeeCode ?? string.Empty,
            q.PlanId,
            planNameById.GetValueOrDefault(q.PlanId, string.Empty),
            q.MeasurementType, q.Amount.Amount, q.Amount.Currency,
            q.Period.Start, q.Period.End, q.Status.ToString(), q.Notes, q.CreatedAt))
            .ToList();

        return Result<PagedResult<QuotaSummaryDto>>.Success(new PagedResult<QuotaSummaryDto>
        {
            Items = dtos,
            TotalCount = totalCount,
            Page = p.Page,
            PageSize = p.PageSize,
        });
    }
}
