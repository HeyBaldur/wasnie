using MediatR;
using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Compensation.Common;
using Wasnie.Application.Compensation.Queries.Payouts;
using Wasnie.Application.Compensation.Queries.Transactions;
using Wasnie.Domain.Authorization;
using Wasnie.Domain.Common.Results;

namespace Wasnie.Application.Compensation.Handlers.Payouts;

/// <summary>
/// The workbook that goes to accounting: what to pay, and what each figure is made of.
///
/// ★★ IT IS THE FILE SOMEBODY PAYS FROM. One row per payee was enough to raise a payment and not
/// enough to defend it: "where does €19,481.02 come from?" could only be answered by opening each
/// payout on screen, one payee at a time, which for a run of two hundred people is not an answer.
/// </summary>
public sealed class ExportPayoutsHandler(
    IApplicationDbContext db,
    IAuthorizationService authorizationService,
    ITenantContext tenantContext,
    IPayoutExcelExportService excelService)
    : IRequestHandler<ExportPayoutsQuery, Result<ExportResult>>
{
    private const int MaxExportRows = 50_000;

    /// <summary>
    /// Ceiling on the Detail sheet.
    ///
    /// ★ IT REFUSES, IT DOES NOT TRUNCATE. A workbook silently missing the lines behind some of its
    /// totals is worse than no workbook: the Summary would still add up, so nothing would look
    /// wrong (§B1). The reader narrows the filter and exports again.
    /// </summary>
    internal const int MaxDetailRows = 200_000;

    public async Task<Result<ExportResult>> Handle(
        ExportPayoutsQuery request, CancellationToken cancellationToken)
    {
        await authorizationService.RequireAsync(Permission.PayoutsExport, cancellationToken);

        var query = ListPayoutsHandler.BuildQuery(db, request.Filter);

        var count = await query.CountAsync(cancellationToken);
        if (count > MaxExportRows)
            return Result<ExportResult>.Failure($"EXPORT_TOO_LARGE:{count}");

        // The lines come with the payouts: they are the Detail sheet, and fetching them per payout
        // would be one round trip per payee.
        var payouts = await query
            .Include(p => p.Lines)
            .OrderByDescending(p => p.UpdatedAt)
            .ToListAsync(cancellationToken);

        var lineCount = payouts.Sum(p => p.Lines.Count);
        if (lineCount > MaxDetailRows)
            return Result<ExportResult>.Failure($"EXPORT_TOO_LARGE:{lineCount}");

        var planNames = await PayoutExportProjection.LoadPlanNamesAsync(db, payouts, cancellationToken);
        var rows = PayoutExportProjection.BuildSummary(payouts, planNames);
        var detail = await PayoutExportProjection.BuildDetailAsync(db, payouts, planNames, cancellationToken);

        var tenant = await db.Tenants
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == tenantContext.TenantId, cancellationToken);
        var slug = tenant?.Slug ?? tenantContext.TenantId.ToString("N")[..8];

        var bytes = excelService.GenerateExcel(rows, detail, slug);
        var fileName = $"payouts-export-{DateTime.UtcNow:yyyy-MM-dd}-{slug}.xlsx";

        return Result<ExportResult>.Success(
            new ExportResult(bytes, fileName, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"));
    }
}
