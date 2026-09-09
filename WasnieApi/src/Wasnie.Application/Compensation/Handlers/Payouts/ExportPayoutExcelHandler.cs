using MediatR;
using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Compensation.Common;
using Wasnie.Application.Compensation.DTOs;
using Wasnie.Application.Compensation.Queries.Payouts;
using Wasnie.Application.Compensation.Queries.Transactions;   // ExportResult lives here
using Wasnie.Domain.Authorization;
using Wasnie.Domain.Common.Results;

namespace Wasnie.Application.Compensation.Handlers.Payouts;

/// <summary>
/// One payout as a spreadsheet: the same document the PDF renders, in the form somebody can actually
/// reconcile against.
///
/// ★ SAME PERMISSION, SAME LOADER AS THE PDF. It is the same document; a reader allowed to take it as
/// a PDF is allowed to take it as a sheet, and both come from
/// <see cref="PayoutExportDtoBuilder"/> so the totals cannot differ between the two files.
/// </summary>
public sealed class ExportPayoutExcelHandler(
    IApplicationDbContext db,
    IAuthorizationService authorizationService,
    IPayoutExcelExportService excelService,
    ITenantContext tenantContext,
    IIdentityService identityService)
    : IRequestHandler<ExportPayoutExcelQuery, Result<ExportResult>>
{
    public async Task<Result<ExportResult>> Handle(
        ExportPayoutExcelQuery request, CancellationToken cancellationToken)
    {
        await authorizationService.RequireAsync(Permission.PayoutsExport, cancellationToken);

        var dto = await PayoutExportDtoBuilder.LoadAsync(db, identityService, request.Id, cancellationToken);

        if (dto is null)
            return Result<ExportResult>.Failure("Payout not found.");

        var tenant = await db.Tenants
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == tenantContext.TenantId, cancellationToken);
        var slug = tenant?.Slug ?? tenantContext.TenantId.ToString("N")[..8];

        var bytes = excelService.GenerateSinglePayoutExcel(dto, slug);

        // Deliberately the PDF's naming, extension aside: the two files of one payout sort next to each
        // other in a download folder instead of looking like two unrelated documents.
        var fileName = $"payout-{dto.PayeeCode}-{dto.PeriodStart:yyyy-MM}.xlsx";

        return Result<ExportResult>.Success(new ExportResult(
            bytes, fileName,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"));
    }
}
