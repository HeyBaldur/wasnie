using MediatR;
using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Common.Extensions;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Common.Models;
using Wasnie.Application.Compensation.DTOs;
using Wasnie.Application.Compensation.Queries.Payouts;
using Wasnie.Domain.Authorization;
using Wasnie.Domain.Common.Results;
using Wasnie.Domain.Compensation.Enums;
using Wasnie.Domain.Compensation.Payouts;

namespace Wasnie.Application.Compensation.Handlers.Payouts;

public sealed class ListPayoutsHandler(
    IApplicationDbContext db,
    IAuthorizationService authorizationService)
    : IRequestHandler<ListPayoutsQuery, Result<PagedResult<PayoutListItemDto>>>
{
    public async Task<Result<PagedResult<PayoutListItemDto>>> Handle(
        ListPayoutsQuery request, CancellationToken cancellationToken)
    {
        await authorizationService.RequireAsync(Permission.PayoutsRead, cancellationToken);

        var f = request.Filter;
        var query = BuildQuery(db, f);

        var sortBy = f.SortBy?.ToLowerInvariant() ?? "updatedat";
        var desc = !string.Equals(f.SortOrder, "asc", StringComparison.OrdinalIgnoreCase);

        query = sortBy switch
        {
            "totalcommission" => desc
                ? query.OrderByDescending(p => p.TotalCommission.Amount)
                : query.OrderBy(p => p.TotalCommission.Amount),
            "calculatedat" => desc
                ? query.OrderByDescending(p => p.CalculatedAt)
                : query.OrderBy(p => p.CalculatedAt),
            "status" => desc
                ? query.OrderByDescending(p => p.Status)
                : query.OrderBy(p => p.Status),
            _ => desc
                ? query.OrderByDescending(p => p.CalculatedAt)
                : query.OrderBy(p => p.CalculatedAt),
        };

        var paged = await query.ToPagedResultAsync(f.Page, f.PageSize, cancellationToken);

        var planIds = paged.Items.Select(p => p.PlanId).Distinct().ToList();
        var planLookup = await db.CompensationPlans
            .Where(p => planIds.Contains(p.Id))
            .Select(p => new { p.Id, p.Name })
            .ToDictionaryAsync(p => p.Id, cancellationToken);

        var dtos = paged.Items.Select(p =>
        {
            planLookup.TryGetValue(p.PlanId, out var plan);
            return new PayoutListItemDto(
                Id: p.Id,
                PayeeId: p.PayeeId,
                PayeeName: p.PayeeSnapshot.FullName,
                PayeeCode: p.PayeeSnapshot.EmployeeCode,
                PlanId: p.PlanId,
                PlanName: plan?.Name ?? p.PlanId.ToString("N")[..8],
                PeriodStart: p.Period.Start,
                PeriodEnd: p.Period.End,
                TotalCommissionAmount: p.TotalCommission.Amount,
                TotalCommissionCurrency: p.TotalCommission.Currency,
                Status: p.Status.ToString(),
                CalculatedAt: p.CalculatedAt,
                CalculatedBy: p.CalculatedBy,
                UpdatedAt: p.UpdatedAt,
                UpdatedBy: p.UpdatedBy,
                PaidAt: p.PaidAt);
        }).ToList();

        return Result<PagedResult<PayoutListItemDto>>.Success(new PagedResult<PayoutListItemDto>
        {
            Items = dtos,
            TotalCount = paged.TotalCount,
            Page = paged.Page,
            PageSize = paged.PageSize,
        });
    }

    internal static IQueryable<CompensationPayout> BuildQuery(
        IApplicationDbContext db, PayoutFilterQuery f)
    {
        var query = db.CompensationPayouts.AsQueryable();

        if (f.PayRunId.HasValue)
            query = query.Where(p => p.PayRunId == f.PayRunId.Value);

        if (!string.IsNullOrWhiteSpace(f.Status) &&
            f.Status != "All" &&
            Enum.TryParse<CompensationPayoutStatus>(f.Status, out var status))
        {
            query = query.Where(p => p.Status == status);
        }

        if (!string.IsNullOrWhiteSpace(f.PayeeIds))
        {
            var ids = ParseGuids(f.PayeeIds);
            if (ids.Count > 0) query = query.Where(p => ids.Contains(p.PayeeId));
        }

        if (!string.IsNullOrWhiteSpace(f.PlanIds))
        {
            var ids = ParseGuids(f.PlanIds);
            if (ids.Count > 0) query = query.Where(p => ids.Contains(p.PlanId));
        }

        if (!string.IsNullOrWhiteSpace(f.Currencies))
        {
            var curs = f.Currencies.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim().ToUpperInvariant())
                .Where(s => s.Length == 3)
                .ToList();
            if (curs.Count > 0)
                query = query.Where(p => curs.Contains(p.TotalCommission.Currency));
        }

        // Period = the COMPENSATION period the payout covers, not the instant it was calculated.
        //
        // It used to filter on CalculatedAt, which produced a false negative with real money behind
        // it: a January payout consolidated on 1 February fell outside a January filter — in this
        // list AND in the export that feeds payroll, so the payee simply did not appear. Payroll
        // cares which fiscal month the money belongs to, not when a background job happened to
        // consolidate it.
        //
        // Intersection, not containment, and in that exact form — the same predicate the rest of the
        // domain uses for a period against a range (GetDashboardSummaryHandler.PayoutsInPeriodRawAsync,
        // CalculatePayoutsForPeriodHandler's assignment overlap). A payout spanning a quarter must
        // show up when someone filters for one month inside it.
        if (f.PeriodFrom.HasValue)
        {
            var from = f.PeriodFrom.Value;
            query = query.Where(p => p.Period.End >= from);
        }

        if (f.PeriodTo.HasValue)
        {
            var to = f.PeriodTo.Value;
            query = query.Where(p => p.Period.Start <= to);
        }

        // PAYMENT-date window (cash flow) — containment on PaidAt, NOT intersection on Period. This is the
        // predicate the dashboard's "commission payouts" card sums, and the card deep-links here with the
        // same two values, so both must agree to the cent. If you change one, change
        // GetDashboardSummaryHandler.PayoutsInPeriodRawAsync in the same commit.
        //
        // The upper bound covers the whole end day: PaidAt is an instant, so comparing it against midnight
        // would drop every payment made during the final day of the window.
        if (f.PaidFrom.HasValue)
        {
            var paidFrom = new DateTimeOffset(
                f.PaidFrom.Value.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc), TimeSpan.Zero);
            query = query.Where(p => p.PaidAt >= paidFrom);
        }

        if (f.PaidTo.HasValue)
        {
            var paidTo = new DateTimeOffset(
                f.PaidTo.Value.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc), TimeSpan.Zero);
            query = query.Where(p => p.PaidAt <= paidTo);
        }

        if (f.AmountMin.HasValue)
            query = query.Where(p => p.TotalCommission.Amount >= f.AmountMin.Value);

        if (f.AmountMax.HasValue)
            query = query.Where(p => p.TotalCommission.Amount <= f.AmountMax.Value);

        if (f.ExcludeZero)
            query = query.Where(p => p.TotalCommission.Amount > 0);

        return query;
    }

    private static List<Guid> ParseGuids(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return [];
        return input.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(s => Guid.TryParse(s.Trim(), out var g) ? (Guid?)g : null)
            .Where(g => g.HasValue)
            .Select(g => g!.Value)
            .ToList();
    }
}
