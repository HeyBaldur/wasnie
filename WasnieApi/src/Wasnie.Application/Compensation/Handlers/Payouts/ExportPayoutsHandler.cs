using MediatR;
using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Compensation.DTOs;
using Wasnie.Application.Compensation.Queries.Payouts;
using Wasnie.Application.Compensation.Queries.Transactions;
using Wasnie.Domain.Authorization;
using Wasnie.Domain.Common.Results;

namespace Wasnie.Application.Compensation.Handlers.Payouts;

public sealed class ExportPayoutsHandler(
    IApplicationDbContext db,
    IAuthorizationService authorizationService,
    ITenantContext tenantContext,
    IPayoutExcelExportService excelService)
    : IRequestHandler<ExportPayoutsQuery, Result<ExportResult>>
{
    private const int MaxExportRows = 50_000;

    public async Task<Result<ExportResult>> Handle(
        ExportPayoutsQuery request, CancellationToken cancellationToken)
    {
        await authorizationService.RequireAsync(Permission.PayoutsExport, cancellationToken);

        var query = ListPayoutsHandler.BuildQuery(db, request.Filter);

        var count = await query.CountAsync(cancellationToken);
        if (count > MaxExportRows)
            return Result<ExportResult>.Failure($"EXPORT_TOO_LARGE:{count}");

        var payouts = await query
            .OrderByDescending(p => p.UpdatedAt)
            .ToListAsync(cancellationToken);

        // Batch-fetch plan names in a single query — no N+1.
        var planIds = payouts.Select(p => p.PlanId).Distinct().ToList();
        var planLookup = await db.CompensationPlans
            .Where(p => planIds.Contains(p.Id))
            .Select(p => new { p.Id, p.Name })
            .ToDictionaryAsync(p => p.Id, cancellationToken);

        var rows = payouts.Select(p =>
        {
            planLookup.TryGetValue(p.PlanId, out var plan);
            return new PayoutExportRow(
                Id: p.Id,
                PayeeName: p.PayeeSnapshot.FullName,
                PayeeCode: p.PayeeSnapshot.EmployeeCode,
                PlanName: plan?.Name ?? p.PlanId.ToString("N")[..8],
                PeriodStart: p.Period.Start,
                PeriodEnd: p.Period.End,
                TotalCommissionAmount: p.TotalCommission.Amount,
                TotalCommissionCurrency: p.TotalCommission.Currency,
                Status: p.Status.ToString(),
                CalculatedAt: p.CalculatedAt,
                UpdatedAt: p.UpdatedAt);
        }).ToList();

        var tenant = await db.Tenants
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == tenantContext.TenantId, cancellationToken);
        var slug = tenant?.Slug ?? tenantContext.TenantId.ToString("N")[..8];

        var bytes = excelService.GenerateExcel(rows, slug);
        var fileName = $"payouts-export-{DateTime.UtcNow:yyyy-MM-dd}-{slug}.xlsx";

        return Result<ExportResult>.Success(
            new ExportResult(bytes, fileName, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"));
    }
}
