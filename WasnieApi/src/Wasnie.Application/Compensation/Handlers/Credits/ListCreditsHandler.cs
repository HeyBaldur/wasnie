using MediatR;
using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Common.Extensions;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Common.Models;
using Wasnie.Application.Compensation.DTOs;
using Wasnie.Application.Compensation.Queries.Credits;
using Wasnie.Domain.Authorization;
using Wasnie.Domain.Common.Results;
using Wasnie.Domain.Compensation.Credits;

namespace Wasnie.Application.Compensation.Handlers.Credits;

public sealed class ListCreditsHandler(
    IApplicationDbContext db,
    IAuthorizationService authorizationService)
    : IRequestHandler<ListCreditsQuery, Result<PagedResult<CreditListDto>>>
{
    public async Task<Result<PagedResult<CreditListDto>>> Handle(
        ListCreditsQuery request, CancellationToken cancellationToken)
    {
        await authorizationService.RequireAsync(Permission.CreditsRead, cancellationToken);

        var query = BuildQuery(db, request.Filter);

        var unfilteredTotal = await db.Credits.CountAsync(cancellationToken);

        var sortBy = request.Filter.SortBy?.ToLower() ?? "allocatedat";
        var desc = !string.Equals(request.Filter.SortOrder, "asc", StringComparison.OrdinalIgnoreCase);

        query = sortBy switch
        {
            "crediteramount" => desc ? query.OrderByDescending(c => c.CreditedAmount.Amount) : query.OrderBy(c => c.CreditedAmount.Amount),
            "originalamount" => desc ? query.OrderByDescending(c => c.OriginalAmount.Amount) : query.OrderBy(c => c.OriginalAmount.Amount),
            _ => desc ? query.OrderByDescending(c => c.AllocatedAt) : query.OrderBy(c => c.AllocatedAt),
        };

        var paged = await query.ToPagedResultAsync(request.Filter.Page, request.Filter.PageSize, cancellationToken);

        var dtos = await EnrichPageAsync(db, paged.Items, request.Filter, cancellationToken);

        return Result<PagedResult<CreditListDto>>.Success(new PagedResult<CreditListDto>
        {
            Items = dtos,
            TotalCount = paged.TotalCount,
            Page = paged.Page,
            PageSize = paged.PageSize,
            UnfilteredTotal = unfilteredTotal,
        });
    }

    internal static IQueryable<Credit> BuildQuery(IApplicationDbContext db, CreditFilterQuery f)
    {
        var query = db.Credits.AsQueryable();

        // Status filter (default: Active only)
        var status = f.Status?.ToLower() ?? "active";
        if (status == "active")
            query = query.Where(c => c.SupersededAt == null);
        else if (status == "superseded")
            query = query.Where(c => c.SupersededAt != null);
        // "all" — no filter

        if (!string.IsNullOrWhiteSpace(f.PayeeIds))
        {
            var ids = ParseGuids(f.PayeeIds);
            if (ids.Count > 0)
                query = query.Where(c => ids.Contains(c.PayeeId));
        }

        if (!string.IsNullOrWhiteSpace(f.PlanIds))
        {
            var ids = ParseGuids(f.PlanIds);
            if (ids.Count > 0)
                query = query.Where(c => ids.Contains(c.PlanId));
        }

        if (f.AllocatedFrom.HasValue)
            query = query.Where(c => c.AllocatedAt >= f.AllocatedFrom.Value.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));

        if (f.AllocatedTo.HasValue)
            query = query.Where(c => c.AllocatedAt < f.AllocatedTo.Value.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));

        if (f.AmountMin.HasValue)
            query = query.Where(c => c.CreditedAmount.Amount >= f.AmountMin.Value);

        if (f.AmountMax.HasValue)
            query = query.Where(c => c.CreditedAmount.Amount <= f.AmountMax.Value);

        if (!string.IsNullOrWhiteSpace(f.Currencies))
        {
            var curs = f.Currencies.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim().ToUpperInvariant())
                .Where(s => s.Length == 3)
                .ToList();
            if (curs.Count > 0)
                query = query.Where(c => curs.Contains(c.CreditedAmount.Currency));
        }

        if (!string.IsNullOrWhiteSpace(f.RuleIds))
        {
            var ids = ParseGuids(f.RuleIds);
            if (ids.Count > 0)
                query = query.Where(c => ids.Contains(c.RuleId));
        }

        // Reference filter requires a subquery against CompensationTransactions.
        if (!string.IsNullOrWhiteSpace(f.Reference))
        {
            var refLower = f.Reference.Trim().ToLower();
            var matchingTxIds = db.CompensationTransactions
                .Where(t => t.ReferenceNumber.ToLower().Contains(refLower))
                .Select(t => t.Id);
            query = query.Where(c => matchingTxIds.Contains(c.TransactionId));
        }

        return query;
    }

    internal static async Task<List<CreditListDto>> EnrichPageAsync(
        IApplicationDbContext db,
        IReadOnlyList<Credit> credits,
        CreditFilterQuery filter,
        CancellationToken ct)
    {
        var txIds = credits.Select(c => c.TransactionId).Distinct().ToList();
        var payeeIds = credits.Select(c => c.PayeeId).Distinct().ToList();
        var planIds = credits.Select(c => c.PlanId).Distinct().ToList();

        var txLookup = await db.CompensationTransactions
            .Where(t => txIds.Contains(t.Id))
            .Select(t => new { t.Id, t.ReferenceNumber })
            .ToDictionaryAsync(t => t.Id, ct);

        var payeeLookup = await db.Payees
            .Where(p => payeeIds.Contains(p.Id))
            .Select(p => new { p.Id, p.FullName, p.EmployeeCode })
            .ToDictionaryAsync(p => p.Id, ct);

        var planLookup = await db.CompensationPlans
            .Where(p => planIds.Contains(p.Id))
            .Select(p => new { p.Id, p.Name })
            .ToDictionaryAsync(p => p.Id, ct);

        return credits.Select(c =>
        {
            txLookup.TryGetValue(c.TransactionId, out var tx);
            payeeLookup.TryGetValue(c.PayeeId, out var payee);
            planLookup.TryGetValue(c.PlanId, out var plan);

            return new CreditListDto(
                Id: c.Id,
                TransactionId: c.TransactionId,
                ReferenceNumber: tx?.ReferenceNumber ?? c.TransactionId.ToString("N")[..8],
                PayeeId: c.PayeeId,
                PayeeName: payee?.FullName,
                PayeeCode: payee?.EmployeeCode,
                PlanId: c.PlanId,
                PlanName: plan?.Name ?? c.RuleSnapshot.PlanId.ToString("N")[..8],
                RuleName: c.RuleSnapshot.RuleName,
                OriginalAmount: c.OriginalAmount.Amount,
                OriginalCurrency: c.OriginalAmount.Currency,
                CreditedAmount: c.CreditedAmount.Amount,
                CreditedCurrency: c.CreditedAmount.Currency,
                AllocatedAt: c.AllocatedAt,
                IsSuperseded: c.SupersededAt.HasValue);
        }).ToList();
    }

    internal static List<Guid> ParseGuids(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return [];
        return input.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(s => Guid.TryParse(s.Trim(), out var g) ? (Guid?)g : null)
            .Where(g => g.HasValue)
            .Select(g => g!.Value)
            .ToList();
    }
}
