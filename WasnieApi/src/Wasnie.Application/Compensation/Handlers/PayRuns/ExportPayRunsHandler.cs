using MediatR;
using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Compensation.Common;
using Wasnie.Application.Compensation.DTOs;
using Wasnie.Application.Compensation.Queries.PayRuns;
using Wasnie.Application.Compensation.Queries.Transactions;
using Wasnie.Domain.Authorization;
using Wasnie.Domain.Common.Results;

namespace Wasnie.Application.Compensation.Handlers.PayRuns;

public sealed class ExportPayRunsHandler(
    IApplicationDbContext db,
    IAuthorizationService authorizationService,
    ITenantContext tenantContext,
    IPayRunExcelExportService excelService)
    : IRequestHandler<ExportPayRunsQuery, Result<ExportResult>>
{
    private const int MaxExportRows = 50_000;

    public async Task<Result<ExportResult>> Handle(
        ExportPayRunsQuery request, CancellationToken cancellationToken)
    {
        await authorizationService.RequireAsync(Permission.PayoutsExport, cancellationToken);

        var query = ListPayRunsHandler.BuildQuery(db, request.Filter)
            .OrderByDescending(r => r.PeriodStart);

        var count = await query.CountAsync(cancellationToken);
        if (count > MaxExportRows)
            return Result<ExportResult>.Failure($"EXPORT_TOO_LARGE:{count}");

        var runs = await query.ToListAsync(cancellationToken);

        var rows = runs.Select(r => new PayRunExportRow(
            Id: r.Id,
            PeriodStart: r.PeriodStart,
            PeriodEnd: r.PeriodEnd,
            Status: r.Status.ToString(),
            PayeeCount: r.PayeeCount,
            PaidPayeeCount: r.PaidPayeeCount,
            ZeroPayoutCount: r.ZeroPayoutCount,
            CreatedBy: r.CreatedBy,
            CreatedAt: r.CreatedAt,
            ApprovedBy: r.ApprovedBy,
            ApprovedAt: r.ApprovedAt,
            PaidBy: r.PaidBy,
            PaidAt: r.PaidAt,
            TotalAmounts: r.TotalAmounts)).ToList();

        // ★★ THE SHEETS THAT NAME PEOPLE. Filtering to Paid and exporting used to produce one row per
        //    run: how much was paid in total and who pressed the button, and not a single payee. The
        //    reader could see that four runs were paid and not who had been paid.
        //
        // ★ SCOPED TO THE RUNS BEING EXPORTED, so the file always agrees with the rows above it. Any
        //   other filter here would let the first sheet and the other two describe different money.
        var runIds = runs.Select(r => r.Id).ToList();

        var payouts = await db.CompensationPayouts
            .Where(p => p.PayRunId != null && runIds.Contains(p.PayRunId!.Value))
            .Include(p => p.Lines)
            .OrderBy(p => p.PayeeSnapshot.FullName)
            .ToListAsync(cancellationToken);

        var lineCount = payouts.Sum(p => p.Lines.Count);
        if (lineCount > Payouts.ExportPayoutsHandler.MaxDetailRows)
            return Result<ExportResult>.Failure($"EXPORT_TOO_LARGE:{lineCount}");

        var planNames = await PayoutExportProjection.LoadPlanNamesAsync(db, payouts, cancellationToken);
        var payees = PayoutExportProjection.BuildSummary(payouts, planNames);
        var detail = await PayoutExportProjection.BuildDetailAsync(db, payouts, planNames, cancellationToken);

        var tenant = await db.Tenants
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == tenantContext.TenantId, cancellationToken);
        var slug = tenant?.Slug ?? tenantContext.TenantId.ToString("N")[..8];

        var bytes = excelService.GenerateExcel(rows, payees, detail, slug);
        var fileName = $"pay-runs-export-{DateTime.UtcNow:yyyy-MM-dd}-{slug}.xlsx";

        return Result<ExportResult>.Success(
            new ExportResult(bytes, fileName, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"));
    }
}
