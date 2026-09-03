using MediatR;
using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Compensation.DTOs;
using Wasnie.Application.Compensation.Queries.Reconciliation;
using Wasnie.Application.Compensation.Queries.Transactions;
using Wasnie.Domain.Authorization;
using Wasnie.Domain.Common.Results;

namespace Wasnie.Application.Compensation.Handlers.Reconciliation;

/// <summary>
/// The filtered queue as a flat spreadsheet.
///
/// ★★ THE SAME FILTER, THE SAME QUERY, THE WHOLE SET. It calls
/// <see cref="ReconciliationQuery.Filtered"/> exactly as the list does and then does NOT page: an
/// export that quietly shipped page one would be the worst kind of wrong, because it looks complete.
///
/// ★ ONE ROW PER ENTRY, REASONS JOINED INTO ONE CELL. No nested grouping, no repeated header blocks —
/// a pure rectangle, because the file exists to be pivoted and filtered by somebody in Excel, and a
/// pretty report is useless for that.
/// </summary>
public sealed class ExportReconciliationHandler(
    IApplicationDbContext db,
    IAuthorizationService authorizationService,
    ITenantContext tenantContext,
    IReconciliationExcelExportService excelService)
    : IRequestHandler<ExportReconciliationQuery, Result<ExportResult>>
{
    private const int MaxExportRows = 50_000;

    public async Task<Result<ExportResult>> Handle(
        ExportReconciliationQuery request, CancellationToken ct)
    {
        await authorizationService.RequireAsync(Permission.ReportsViewAll, ct);

        var seeds = ReconciliationQuery.Filtered(db, request.Filter);

        var distinctCount = await seeds
            .Select(s => new { s.Kind, s.EntityId })
            .Distinct()
            .CountAsync(ct);

        if (distinctCount > MaxExportRows)
            return Result<ExportResult>.Failure($"EXPORT_TOO_LARGE:{distinctCount}");

        var all = await seeds.ToListAsync(ct);

        var payeeIds = all.Where(s => s.PayeeId.HasValue).Select(s => s.PayeeId!.Value).Distinct().ToList();
        var planIds = all.Where(s => s.PlanId.HasValue).Select(s => s.PlanId!.Value).Distinct().ToList();
        var creditIds = all.Where(s => s.Kind == ReconciliationQuery.KindCredit).Select(s => s.EntityId).Distinct().ToList();
        var txIds = all.Where(s => s.Kind == ReconciliationQuery.KindTransaction).Select(s => s.EntityId).Distinct().ToList();

        var payees = await db.Payees
            .Where(p => payeeIds.Contains(p.Id))
            .Select(p => new { p.Id, p.FullName, p.EmployeeCode })
            .ToDictionaryAsync(p => p.Id, ct);

        var plans = await db.CompensationPlans
            .Where(p => planIds.Contains(p.Id))
            .Select(p => new { p.Id, p.Name })
            .ToDictionaryAsync(p => p.Id, ct);

        var creditTx = await db.Credits
            .Where(c => creditIds.Contains(c.Id))
            .Select(c => new { c.Id, c.TransactionId })
            .ToDictionaryAsync(c => c.Id, c => c.TransactionId, ct);

        var refIds = txIds.Concat(creditTx.Values).Distinct().ToList();
        var references = await db.CompensationTransactions
            .Where(t => refIds.Contains(t.Id))
            .Select(t => new { t.Id, t.ReferenceNumber })
            .ToDictionaryAsync(t => t.Id, t => t.ReferenceNumber, ct);

        var rows = all
            .GroupBy(s => (s.Kind, s.EntityId))
            .Select(g =>
            {
                var lead = g.First();
                var money = g.FirstOrDefault(s => s.MoneyKind != ReconciliationQuery.MoneyNone);

                string reference = string.Empty;
                if (lead.Kind == ReconciliationQuery.KindCredit
                    && creditTx.TryGetValue(lead.EntityId, out var txId)
                    && references.TryGetValue(txId, out var creditRef))
                {
                    reference = creditRef;
                }
                else if (lead.Kind == ReconciliationQuery.KindTransaction
                    && references.TryGetValue(lead.EntityId, out var txRef))
                {
                    reference = txRef;
                }

                payees.TryGetValue(lead.PayeeId ?? Guid.Empty, out var payee);
                plans.TryGetValue(lead.PlanId ?? Guid.Empty, out var plan);

                return new ReconciliationExportRow(
                    Kind: ((ReconciliationEntryKind)lead.Kind).ToString(),
                    EntityId: lead.EntityId.ToString(),
                    ReferenceNumber: reference,
                    PayeeName: payee?.FullName ?? string.Empty,
                    PayeeCode: payee?.EmployeeCode ?? string.Empty,
                    PlanName: plan?.Name ?? string.Empty,
                    Amount: money?.Amount,
                    Currency: money?.Currency ?? string.Empty,
                    MoneyKind: ((ReconciliationMoneyKind)(money?.MoneyKind ?? ReconciliationQuery.MoneyNone)).ToString(),
                    PeriodDate: lead.PeriodDate?.ToString("yyyy-MM-dd") ?? string.Empty,
                    OccurredAt: g.Max(s => s.OccurredAt),
                    // ★ CODES, not sentences: the file is data, and whoever reads it decides the
                    // language. A translated spreadsheet cannot be filtered by a formula.
                    Reasons: string.Join("; ", g.Select(s => s.Reason).Distinct().OrderBy(r => r)));
            })
            .OrderByDescending(r => r.OccurredAt)
            .ThenBy(r => r.EntityId)
            .ToList();

        var tenant = await db.Tenants
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == tenantContext.TenantId, ct);
        var slug = tenant?.Slug ?? tenantContext.TenantId.ToString("N")[..8];

        var bytes = excelService.GenerateExcel(rows, slug);
        var fileName = $"reconciliation-{DateTime.UtcNow:yyyy-MM-dd}-{slug}.xlsx";

        return Result<ExportResult>.Success(new ExportResult(
            bytes, fileName, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"));
    }
}
