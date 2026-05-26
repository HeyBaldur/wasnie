using MediatR;
using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Common.Models;
using Wasnie.Application.Compensation.DTOs;
using Wasnie.Application.Compensation.Queries.Quotas;
using Wasnie.Domain.Common.Results;
using Wasnie.Domain.Compensation.Enums;

namespace Wasnie.Application.Compensation.Handlers.Quotas;

public sealed class ListQuotasHandler(IApplicationDbContext db)
    : IRequestHandler<ListQuotasQuery, Result<PagedResult<QuotaSummaryDto>>>
{
    private static readonly HashSet<string> AllowedSortFields =
        new(StringComparer.OrdinalIgnoreCase) { "payeefullname", "periodstart", "amount" };

    public async Task<Result<PagedResult<QuotaSummaryDto>>> Handle(ListQuotasQuery request, CancellationToken cancellationToken)
    {
        var p = request.Pagination;
        var query = db.Quotas.AsQueryable();

        // Filters
        if (p.Filters != null)
        {
            if (p.Filters.TryGetValue("status", out var statusStr) &&
                Enum.TryParse<QuotaStatus>(statusStr, ignoreCase: true, out var status))
                query = query.Where(x => x.Status == status);

            if (p.Filters.TryGetValue("payeeid", out var payeeIdStr) &&
                Guid.TryParse(payeeIdStr, out var payeeId))
                query = query.Where(x => x.PayeeId == payeeId);
        }

        // Join with payees for search / sort
        var joined = query.Join(
            db.Payees,
            q => q.PayeeId,
            p2 => p2.Id,
            (q, p2) => new
            {
                Quota = q,
                PayeeFullName = p2.FullName,
                PayeeEmployeeCode = p2.EmployeeCode,
            });

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
            "amount"        => desc ? joined.OrderByDescending(x => x.Quota.Amount.Amount) : joined.OrderBy(x => x.Quota.Amount.Amount),
            _               => desc ? joined.OrderByDescending(x => x.Quota.Period.Start) : joined.OrderBy(x => x.Quota.Period.Start),
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
