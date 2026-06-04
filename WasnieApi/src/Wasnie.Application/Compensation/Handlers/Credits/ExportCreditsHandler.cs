using MediatR;
using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Compensation.DTOs;
using Wasnie.Application.Compensation.Queries.Credits;
using Wasnie.Application.Compensation.Queries.Transactions;
using Wasnie.Domain.Authorization;
using Wasnie.Domain.Common.Results;

namespace Wasnie.Application.Compensation.Handlers.Credits;

public sealed class ExportCreditsHandler(
    IApplicationDbContext db,
    IAuthorizationService authorizationService,
    ITenantContext tenantContext,
    ICreditExcelExportService excelService)
    : IRequestHandler<ExportCreditsQuery, Result<ExportResult>>
{
    private const int MaxExportRows = 50_000;

    public async Task<Result<ExportResult>> Handle(
        ExportCreditsQuery request, CancellationToken cancellationToken)
    {
        await authorizationService.RequireAsync(Permission.CreditsExport, cancellationToken);

        var query = ListCreditsHandler.BuildQuery(db, request.Filter);

        var count = await query.CountAsync(cancellationToken);
        if (count > MaxExportRows)
            return Result<ExportResult>.Failure($"EXPORT_TOO_LARGE:{count}");

        var credits = await query
            .OrderByDescending(c => c.AllocatedAt)
            .ToListAsync(cancellationToken);

        // Batch-fetch related entities in single queries — no N+1.
        var txIds = credits.Select(c => c.TransactionId).Distinct().ToList();
        var payeeIds = credits.Select(c => c.PayeeId).Distinct().ToList();
        var planIds = credits.Select(c => c.PlanId).Distinct().ToList();

        var txLookup = await db.CompensationTransactions
            .Where(t => txIds.Contains(t.Id))
            .Select(t => new { t.Id, t.ReferenceNumber })
            .ToDictionaryAsync(t => t.Id, cancellationToken);

        var payeeLookup = await db.Payees
            .Where(p => payeeIds.Contains(p.Id))
            .Select(p => new { p.Id, p.FullName, p.EmployeeCode })
            .ToDictionaryAsync(p => p.Id, cancellationToken);

        var planLookup = await db.CompensationPlans
            .Where(p => planIds.Contains(p.Id))
            .Select(p => new { p.Id, p.Name })
            .ToDictionaryAsync(p => p.Id, cancellationToken);

        var rows = credits.Select(c =>
        {
            txLookup.TryGetValue(c.TransactionId, out var tx);
            payeeLookup.TryGetValue(c.PayeeId, out var payee);
            planLookup.TryGetValue(c.PlanId, out var plan);

            return new CreditExportRow(
                Id: c.Id,
                ReferenceNumber: tx?.ReferenceNumber ?? c.TransactionId.ToString("N")[..8],
                PayeeName: payee?.FullName,
                PayeeCode: payee?.EmployeeCode,
                PlanName: plan?.Name ?? c.RuleSnapshot.PlanId.ToString("N")[..8],
                RuleName: c.RuleSnapshot.RuleName,
                OriginalAmount: c.OriginalAmount.Amount,
                OriginalCurrency: c.OriginalAmount.Currency,
                CreditedAmount: c.CreditedAmount.Amount,
                CreditedCurrency: c.CreditedAmount.Currency,
                SplitPercentage: c.SplitPercentage.ToPercent(),
                Role: c.Role.ToString(),
                AllocatedAt: c.AllocatedAt,
                AllocatedBy: c.AllocatedBy,
                Status: c.SupersededAt.HasValue ? "Superseded" : "Active",
                SupersededAt: c.SupersededAt,
                SupersededBy: c.SupersededBy);
        }).ToList();

        var tenant = await db.Tenants
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == tenantContext.TenantId, cancellationToken);
        var slug = tenant?.Slug ?? tenantContext.TenantId.ToString("N")[..8];

        var bytes = excelService.GenerateExcel(rows, slug);
        var fileName = $"credits-export-{DateTime.UtcNow:yyyy-MM-dd}-{slug}.xlsx";

        return Result<ExportResult>.Success(
            new ExportResult(bytes, fileName, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"));
    }
}
