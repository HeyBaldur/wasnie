using MediatR;
using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Compensation.DTOs;
using Wasnie.Application.Compensation.Queries.Payouts;
using Wasnie.Application.Compensation.Queries.Transactions;
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

        var payout = await db.CompensationPayouts
            .Include(p => p.Lines)
            .FirstOrDefaultAsync(p => p.Id == request.Id, cancellationToken);

        if (payout is null)
            return Result<ExportResult>.Failure("Payout not found.");

        var calculatedByDisplay = await ResolveActorDisplayAsync(payout.CalculatedBy);
        var updatedByDisplay    = await ResolveActorDisplayAsync(payout.UpdatedBy);

        var planName = await db.CompensationPlans
            .Where(p => p.Id == payout.PlanId)
            .Select(p => p.Name)
            .FirstOrDefaultAsync(cancellationToken) ?? string.Empty;

        var lines = await GetPayoutByIdHandler.BuildLinesAsync(payout.Lines, db, payout.Id, cancellationToken);

        var dto = new PayoutDto(
            Id: payout.Id,
            TenantId: payout.TenantId,
            PayeeId: payout.PayeeId,
            PayeeName: payout.PayeeSnapshot.FullName,
            PayeeCode: payout.PayeeSnapshot.EmployeeCode,
            PlanId: payout.PlanId,
            PlanName: planName,
            PeriodStart: payout.Period.Start,
            PeriodEnd: payout.Period.End,
            TotalCommissionAmount: payout.TotalCommission.Amount,
            TotalCommissionCurrency: payout.TotalCommission.Currency,
            Status: payout.Status.ToString(),
            CalculatedAt: payout.CalculatedAt,
            CalculatedBy: calculatedByDisplay,
            UpdatedAt: payout.UpdatedAt,
            UpdatedBy: updatedByDisplay,
            Lines: lines);

        var bytes = pdfService.GeneratePdf(dto);
        var fileName = $"payout-{payout.PayeeSnapshot.EmployeeCode}-{payout.Period.Start:yyyy-MM}.pdf";

        return Result<ExportResult>.Success(new ExportResult(bytes, fileName, "application/pdf"));
    }

    // If the stored actor is a GUID (legacy data before the email fix), resolve it to an email.
    // Returns the original value unchanged for emails and "system".
    private async Task<string> ResolveActorDisplayAsync(string actor)
    {
        if (Guid.TryParse(actor, out _))
            return await identityService.FindEmailByUserIdAsync(actor) ?? actor;
        return actor;
    }
}
