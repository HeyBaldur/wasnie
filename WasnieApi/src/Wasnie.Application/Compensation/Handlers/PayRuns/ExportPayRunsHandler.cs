using MediatR;
using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Common.Interfaces;
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

        var tenant = await db.Tenants
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == tenantContext.TenantId, cancellationToken);
        var slug = tenant?.Slug ?? tenantContext.TenantId.ToString("N")[..8];

        var bytes = excelService.GenerateExcel(rows, slug);
        var fileName = $"pay-runs-export-{DateTime.UtcNow:yyyy-MM-dd}-{slug}.xlsx";

        return Result<ExportResult>.Success(
            new ExportResult(bytes, fileName, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"));
    }
}
