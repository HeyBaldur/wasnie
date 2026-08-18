using MediatR;
using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Common.Models;
using Wasnie.Application.Compensation.DTOs;
using Wasnie.Application.Compensation.Queries.Quotas;
using Wasnie.Domain.Authorization;
using Wasnie.Domain.Common.Results;
using Wasnie.Domain.Compensation.Enums;

namespace Wasnie.Application.Compensation.Handlers.Quotas;

public sealed class ListQuotasHandler(
    IApplicationDbContext db,
    IAuthorizationService authorizationService,
    IPayeeAccessGuard payeeAccessGuard)
    : IRequestHandler<ListQuotasQuery, Result<PagedResult<QuotaSummaryDto>>>
{
    private static readonly HashSet<string> AllowedSortFields =
        new(StringComparer.OrdinalIgnoreCase) { "payeefullname", "periodstart", "amount" };

    public async Task<Result<PagedResult<QuotaSummaryDto>>> Handle(ListQuotasQuery request, CancellationToken cancellationToken)
    {
        await authorizationService.RequireAsync(Permission.QuotasRead, cancellationToken);
        var p = request.Pagination;
        var query = db.Quotas.AsQueryable();

        // ★ THE WIDEST OF THE QUOTA LEAKS, AND IT TAKES NO PAYEE ID AT ALL. This list is paged AND
        // searchable by name, so a rep did not even need an id to work with: `?search=` returned any
        // colleague's commission target. Filtered by visibility for the same reason
        // terminated-with-balance is — a list cannot be protected by a per-resource check.
        var visibility = await payeeAccessGuard.GetVisibilityAsync(cancellationToken);
        var visibleIds = visibility.IsUnrestricted ? null : visibility.PayeeIds.ToArray();
        if (visibleIds is not null)
            query = query.Where(x => visibleIds.Contains(x.PayeeId));

        // Filters
        if (!string.IsNullOrWhiteSpace(p.Status) &&
            Enum.TryParse<QuotaStatus>(p.Status, ignoreCase: true, out var status))
            query = query.Where(x => x.Status == status);

        if (p.PayeeId.HasValue)
            query = query.Where(x => x.PayeeId == p.PayeeId);

        // Join with payees and plans for search / sort / display
        var joined = query
            .Join(db.Payees, q => q.PayeeId, p2 => p2.Id,
                (q, p2) => new { Quota = q, PayeeFullName = p2.FullName, PayeeEmployeeCode = p2.EmployeeCode })
            .Join(db.CompensationPlans, x => x.Quota.PlanId, pl => pl.Id,
                (x, pl) => new { x.Quota, x.PayeeFullName, x.PayeeEmployeeCode, PlanName = pl.Name });

        // Search
        if (!string.IsNullOrWhiteSpace(p.Search))
        {
            var srch = p.Search.Trim().ToLower();
            joined = joined.Where(x =>
                x.PayeeFullName.ToLower().Contains(srch) ||
                x.PayeeEmployeeCode.ToLower().Contains(srch));
        }

        // Sort
        var sortBy = AllowedSortFields.Contains(p.SortBy ?? "") ? p.SortBy!.ToLower() : "periodstart";
        var desc = string.Equals(p.SortOrder, "desc", StringComparison.OrdinalIgnoreCase);

        var sorted = sortBy switch
        {
            "payeefullname" => desc ? joined.OrderByDescending(x => x.PayeeFullName) : joined.OrderBy(x => x.PayeeFullName),
            "amount" => desc ? joined.OrderByDescending(x => x.Quota.Amount.Amount) : joined.OrderBy(x => x.Quota.Amount.Amount),
            _ => desc ? joined.OrderByDescending(x => x.Quota.Period.Start) : joined.OrderBy(x => x.Quota.Period.Start),
        };

        var totalCount = await sorted.CountAsync(cancellationToken);
        var items = await sorted
            .Skip((p.Page - 1) * p.PageSize)
            .Take(p.PageSize)
            .ToListAsync(cancellationToken);

        var dtos = items.Select(x => new QuotaSummaryDto(
            x.Quota.Id,
            x.Quota.TenantId,
            x.Quota.PayeeId,
            x.PayeeFullName,
            x.PayeeEmployeeCode,
            x.Quota.PlanId,
            x.PlanName,
            x.Quota.MeasurementType,
            x.Quota.Amount.Amount,
            x.Quota.Amount.Currency,
            x.Quota.Period.Start,
            x.Quota.Period.End,
            x.Quota.Status.ToString(),
            x.Quota.Notes,
            x.Quota.CreatedAt)).ToList();

        return Result<PagedResult<QuotaSummaryDto>>.Success(new PagedResult<QuotaSummaryDto>
        {
            Items = dtos,
            TotalCount = totalCount,
            Page = p.Page,
            PageSize = p.PageSize,
        });
    }
}
