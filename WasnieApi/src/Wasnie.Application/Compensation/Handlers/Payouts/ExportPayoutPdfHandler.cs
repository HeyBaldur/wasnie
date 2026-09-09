using MediatR;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Compensation.Common;
using Wasnie.Application.Compensation.DTOs;
using Wasnie.Application.Compensation.Queries.Payouts;
using Wasnie.Application.Compensation.Queries.Transactions;   // ExportResult lives here
using Wasnie.Domain.Authorization;
using Wasnie.Domain.Common.Results;

namespace Wasnie.Application.Compensation.Handlers.Payouts;

public sealed class ExportPayoutPdfHandler(
    IApplicationDbContext db,
    IAuthorizationService authorizationService,
    IPayoutPdfExportService pdfService,
    IIdentityService identityService)
    : IRequestHandler<ExportPayoutPdfQuery, Result<ExportResult>>
{
    public async Task<Result<ExportResult>> Handle(
        ExportPayoutPdfQuery request, CancellationToken cancellationToken)
    {
        await authorizationService.RequireAsync(Permission.PayoutsExport, cancellationToken);

        // The loading moved to PayoutExportDtoBuilder when the Excel export arrived: two copies of it
        // would eventually put a different total in the two files of the same payout.
        var dto = await PayoutExportDtoBuilder.LoadAsync(db, identityService, request.Id, cancellationToken);

        if (dto is null)
            return Result<ExportResult>.Failure("Payout not found.");

        var bytes = pdfService.GeneratePdf(dto);
        var fileName = $"payout-{dto.PayeeCode}-{dto.PeriodStart:yyyy-MM}.pdf";

        return Result<ExportResult>.Success(new ExportResult(bytes, fileName, "application/pdf"));
    }
}
