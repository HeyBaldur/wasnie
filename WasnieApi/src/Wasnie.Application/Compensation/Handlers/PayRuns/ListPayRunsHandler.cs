using MediatR;
using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Common.Extensions;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Common.Models;
using Wasnie.Application.Compensation.DTOs;
using Wasnie.Application.Compensation.Queries.PayRuns;
using Wasnie.Domain.Authorization;
using Wasnie.Domain.Common.Results;
using Wasnie.Domain.Compensation.Enums;

namespace Wasnie.Application.Compensation.Handlers.PayRuns;

public sealed class ListPayRunsHandler(
    IApplicationDbContext db,
    IAuthorizationService authorizationService)
    : IRequestHandler<ListPayRunsQuery, Result<PagedResult<PayRunListItemDto>>>
{
    public async Task<Result<PagedResult<PayRunListItemDto>>> Handle(
        ListPayRunsQuery request, CancellationToken cancellationToken)
    {
        await authorizationService.RequireAsync(Permission.PayoutsRead, cancellationToken);

        var f = request.Filter;
        var query = BuildQuery(db, f);

        query = string.Equals(f.SortOrder, "asc", StringComparison.OrdinalIgnoreCase)
            ? query.OrderBy(r => r.CreatedAt)
            : query.OrderByDescending(r => r.CreatedAt);

        var paged = await query.ToPagedResultAsync(f.Page, f.PageSize, cancellationToken);

        var dtos = paged.Items.Select(r => new PayRunListItemDto(
            Id: r.Id,
            PeriodStart: r.PeriodStart,
            PeriodEnd: r.PeriodEnd,
            Status: r.Status.ToString(),
            PayeeCount: r.PayeeCount,
            PaidPayeeCount: r.PaidPayeeCount,
            ZeroPayoutCount: r.ZeroPayoutCount,
            TotalAmounts: r.TotalAmounts,
            CreatedAt: r.CreatedAt,
            CreatedBy: r.CreatedBy,
            ApprovedAt: r.ApprovedAt,
            ApprovedBy: r.ApprovedBy,
            PaidAt: r.PaidAt,
            PaidBy: r.PaidBy)).ToList();

        return Result<PagedResult<PayRunListItemDto>>.Success(new PagedResult<PayRunListItemDto>
        {
            Items = dtos,
            TotalCount = paged.TotalCount,
            Page = paged.Page,
            PageSize = paged.PageSize,
        });
    }

    internal static IQueryable<Wasnie.Domain.Compensation.Payouts.PayRun> BuildQuery(
        IApplicationDbContext db, PayRunFilterQuery f)
    {
        var query = db.PayRuns.AsQueryable();

        if (!string.IsNullOrWhiteSpace(f.Status) &&
            Enum.TryParse<PayRunStatus>(f.Status, out var status))
            query = query.Where(r => r.Status == status);

        if (f.PeriodFrom.HasValue)
            query = query.Where(r => r.PeriodStart >= f.PeriodFrom.Value);

        if (f.PeriodTo.HasValue)
            query = query.Where(r => r.PeriodEnd <= f.PeriodTo.Value);

        return query;
    }
}
