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
            SupplementalSequence: r.SupplementalSequence,
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

        // Period = the COMPENSATION period the run covers, not when the row was created.
        //
        // Same fracture as the payouts list had: a January run opened on 1 February fell outside a
        // January filter, so Finance looking for "January" simply did not see the run that holds
        // January's money. Creation time is infrastructure time; the filter asks a fiscal question.
        //
        // Intersection, not containment — identical to ListPayoutsHandler and to
        // GetDashboardSummaryHandler.PayoutsInPeriodRawAsync: a run spanning a quarter must appear
        // when someone filters for one month inside it.
        //
        // PayRun exposes the period as flat DateOnly columns (PayRun.cs:11-12), unlike
        // CompensationPayout which wraps it in a Period value object.
        if (f.PeriodFrom.HasValue)
        {
            var from = f.PeriodFrom.Value;
            query = query.Where(r => r.PeriodEnd >= from);
        }

        if (f.PeriodTo.HasValue)
        {
            var to = f.PeriodTo.Value;
            query = query.Where(r => r.PeriodStart <= to);
        }

        return query;
    }
}
