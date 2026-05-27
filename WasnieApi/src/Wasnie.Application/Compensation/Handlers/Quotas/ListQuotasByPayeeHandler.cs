using MediatR;
using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Common.Extensions;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Common.Models;
using Wasnie.Application.Compensation.DTOs;
using Wasnie.Application.Compensation.Queries.Quotas;
using Wasnie.Domain.Authorization;
using Wasnie.Domain.Common.Results;
using Wasnie.Domain.Compensation.Enums;

namespace Wasnie.Application.Compensation.Handlers.Quotas;

public sealed class ListQuotasByPayeeHandler(IApplicationDbContext db, IAuthorizationService authorizationService)
    : IRequestHandler<ListQuotasByPayeeQuery, Result<PagedResult<QuotaSummaryDto>>>
{
    private static readonly HashSet<string> AllowedSortFields =
        new(StringComparer.OrdinalIgnoreCase) { "periodstart" };

    public async Task<Result<PagedResult<QuotaSummaryDto>>> Handle(ListQuotasByPayeeQuery request, CancellationToken cancellationToken)
    {
        await authorizationService.RequireAsync(Permission.QuotasRead, cancellationToken);
        var p = request.Pagination;
        var query = db.Quotas
            .Where(q => q.PayeeId == request.PayeeId)
            .AsQueryable();

        // Filters
        if (!string.IsNullOrWhiteSpace(p.Status) &&
            Enum.TryParse<QuotaStatus>(p.Status, ignoreCase: true, out var status))
            query = query.Where(x => x.Status == status);

        // Sort
        var desc = string.Equals(p.SortOrder, "desc", StringComparison.OrdinalIgnoreCase);
        // Default: periodstart desc
        query = desc ? query.OrderByDescending(x => x.Period.Start) : query.OrderBy(x => x.Period.Start);

        var paged = await query.ToPagedResultAsync(p.Page, p.PageSize, cancellationToken);

        var payee = await db.Payees.FirstOrDefaultAsync(x => x.Id == request.PayeeId, cancellationToken);

        var dtos = paged.Items.Select(q => new QuotaSummaryDto(
            q.Id,
            q.TenantId,
            q.PayeeId,
            payee?.FullName ?? string.Empty,
            payee?.EmployeeCode ?? string.Empty,
            q.PlanId,
            q.MeasurementType,
            q.Amount.Amount,
            q.Amount.Currency,
            q.Period.Start,
            q.Period.End,
            q.Status.ToString(),
            q.Notes,
            q.CreatedAt)).ToList();

        return Result<PagedResult<QuotaSummaryDto>>.Success(new PagedResult<QuotaSummaryDto>
        {
            Items = dtos,
            TotalCount = paged.TotalCount,
            Page = paged.Page,
            PageSize = paged.PageSize,
        });
    }
}
