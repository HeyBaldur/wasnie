using MediatR;
using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Compensation.DTOs;
using Wasnie.Application.Compensation.Queries.Reconciliation;
using Wasnie.Domain.Authorization;
using Wasnie.Domain.Common.Results;

namespace Wasnie.Application.Compensation.Handlers.Reconciliation;

/// <summary>
/// A page of the reconciliation queue, plus the totals for the whole filtered set behind it.
///
/// ★ READ-ONLY, AND v1 IS READ-ONLY ON PURPOSE. Nothing here resolves, forces or carries anything
/// over: first the money is visible, then somebody decides what to do about it.
/// </summary>
public sealed class GetReconciliationHandler(
    IApplicationDbContext db,
    IAuthorizationService authorizationService)
    : IRequestHandler<GetReconciliationQuery, Result<ReconciliationPageDto>>
{
    private const int MaxPageSize = 200;

    public async Task<Result<ReconciliationPageDto>> Handle(
        GetReconciliationQuery request, CancellationToken ct)
    {
        await authorizationService.RequireAsync(Permission.ReportsViewAll, ct);

        var filter = request.Filter with
        {
            Page = request.Filter.Page < 1 ? 1 : request.Filter.Page,
            PageSize = Math.Clamp(request.Filter.PageSize, 1, MaxPageSize),
        };

        var seeds = ReconciliationQuery.Filtered(db, filter);

        // ── The page's entities, chosen in SQL ───────────────────────────────────────────────
        //
        // ★ PAGING IS OVER DISTINCT ENTITIES, NOT OVER SEEDS. Paging the seeds would put the two
        // halves of a two-reason entry on different pages and make the page sizes lie.
        var keys = await seeds
            .Select(s => new { s.Kind, s.EntityId, s.OccurredAt })
            .Distinct()
            .OrderByDescending(k => k.OccurredAt)
            .ThenBy(k => k.EntityId)
            .Skip((filter.Page - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .ToListAsync(ct);

        var summary = await ReconciliationQuery.SummariseAsync(db, filter, ct);

        if (keys.Count == 0)
        {
            return Result<ReconciliationPageDto>.Success(new ReconciliationPageDto(
                [], filter.Page, filter.PageSize, summary.TotalRows, summary));
        }

        // ── Every seed of those entities, so a row arrives with ALL its reasons ──────────────
        var pageIds = keys.Select(k => k.EntityId).Distinct().ToList();
        var pageSeeds = await seeds
            .Where(s => pageIds.Contains(s.EntityId))
            .ToListAsync(ct);

        var wanted = keys.Select(k => (k.Kind, k.EntityId)).ToHashSet();
        var grouped = pageSeeds
            .Where(s => wanted.Contains((s.Kind, s.EntityId)))
            .GroupBy(s => (s.Kind, s.EntityId))
            .ToDictionary(g => g.Key, g => g.ToList());

        // ── Names, in two batched lookups. No N+1. ───────────────────────────────────────────
        var payeeIds = pageSeeds.Where(s => s.PayeeId.HasValue).Select(s => s.PayeeId!.Value).Distinct().ToList();
        var planIds = pageSeeds.Where(s => s.PlanId.HasValue).Select(s => s.PlanId!.Value).Distinct().ToList();
        var txIds = pageSeeds.Where(s => s.Kind != ReconciliationQuery.KindPlan).Select(s => s.EntityId).Distinct().ToList();
        var creditIds = pageSeeds.Where(s => s.Kind == ReconciliationQuery.KindCredit).Select(s => s.EntityId).Distinct().ToList();

        var payees = await db.Payees
            .Where(p => payeeIds.Contains(p.Id))
            .Select(p => new { p.Id, p.FullName, p.EmployeeCode })
            .ToDictionaryAsync(p => p.Id, ct);

        var plans = await db.CompensationPlans
            .Where(p => planIds.Contains(p.Id))
            .Select(p => new { p.Id, p.Name })
            .ToDictionaryAsync(p => p.Id, ct);

        // A credit's reference is its transaction's; a transaction row's is its own.
        var creditTx = await db.Credits
            .Where(c => creditIds.Contains(c.Id))
            .Select(c => new { c.Id, c.TransactionId })
            .ToDictionaryAsync(c => c.Id, c => c.TransactionId, ct);

        var refLookupIds = txIds.Concat(creditTx.Values).Distinct().ToList();
        var references = await db.CompensationTransactions
            .Where(t => refLookupIds.Contains(t.Id))
            .Select(t => new { t.Id, t.ReferenceNumber })
            .ToDictionaryAsync(t => t.Id, t => t.ReferenceNumber, ct);

        var items = keys.Select(k =>
        {
            var group = grouped[(k.Kind, k.EntityId)];
            var lead = group[0];

            string? reference = null;
            Guid? transactionId = null;
            if (k.Kind == ReconciliationQuery.KindCredit)
            {
                if (creditTx.TryGetValue(k.EntityId, out var txId))
                {
                    transactionId = txId;
                    references.TryGetValue(txId, out reference);
                }
            }
            else if (k.Kind == ReconciliationQuery.KindTransaction)
            {
                transactionId = k.EntityId;
                references.TryGetValue(k.EntityId, out reference);
            }

            payees.TryGetValue(lead.PayeeId ?? Guid.Empty, out var payee);
            plans.TryGetValue(lead.PlanId ?? Guid.Empty, out var plan);

            // The money-bearing seed decides the row's amount; a row whose only seeds carry none
            // shows none. Never a sum across seeds — see SummariseAsync.
            var money = group.FirstOrDefault(s => s.MoneyKind != ReconciliationQuery.MoneyNone);

            return new ReconciliationRowDto(
                Kind: (ReconciliationEntryKind)k.Kind,
                EntityId: k.EntityId,
                TransactionId: transactionId,
                ReferenceNumber: reference,
                PayeeId: lead.PayeeId,
                PayeeName: payee?.FullName,
                PayeeCode: payee?.EmployeeCode,
                PlanId: lead.PlanId,
                PlanName: plan?.Name,
                Amount: money?.Amount,
                Currency: money?.Currency,
                MoneyKind: (ReconciliationMoneyKind)(money?.MoneyKind ?? ReconciliationQuery.MoneyNone),
                PeriodDate: lead.PeriodDate,
                OccurredAt: k.OccurredAt,
                // Ordered so two screens listing the same entry list its reasons the same way.
                Reasons: group.Select(s => s.Reason).Distinct().OrderBy(r => r).ToList());
        }).ToList();

        return Result<ReconciliationPageDto>.Success(new ReconciliationPageDto(
            items, filter.Page, filter.PageSize, summary.TotalRows, summary));
    }
}
