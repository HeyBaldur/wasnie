using MediatR;
using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Authorization;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Compensation.DTOs;
using Wasnie.Application.Compensation.Queries.Quotas;
using Wasnie.Domain.Authorization;
using Wasnie.Domain.Common.Results;

namespace Wasnie.Application.Compensation.Handlers.Quotas;

public sealed class GetQuotaByIdHandler(
    IApplicationDbContext db,
    IAuthorizationService authorizationService,
    IPayeeAccessGuard payeeAccessGuard)
    : IRequestHandler<GetQuotaByIdQuery, Result<QuotaSummaryDto>>
{
    public async Task<Result<QuotaSummaryDto>> Handle(GetQuotaByIdQuery request, CancellationToken cancellationToken)
    {
        await authorizationService.RequireAsync(Permission.QuotasRead, cancellationToken);
        var quota = await db.Quotas
            .FirstOrDefaultAsync(q => q.Id == request.QuotaId, cancellationToken);

        if (quota is null)
            return Result<QuotaSummaryDto>.Failure(PayeeAccessDenied.QuotaMessage);

        // ★ THE PAYEE IS NOT IN THE URL, WHICH IS EXACTLY WHY THIS ENDPOINT NEEDED A GUARD OF ITS OWN.
        // A per-payee check on the route would have missed it entirely: the caller names a QUOTA, and
        // the owner is only known once the row is loaded. Guessable ids are guessable either way.
        if (!await payeeAccessGuard.CanReadAsync(quota.PayeeId, cancellationToken))
            return Result<QuotaSummaryDto>.Failure(PayeeAccessDenied.QuotaMessage);

        var payee = await db.Payees.FirstOrDefaultAsync(p => p.Id == quota.PayeeId, cancellationToken);
        var plan = await db.CompensationPlans.FirstOrDefaultAsync(p => p.Id == quota.PlanId, cancellationToken);

        return Result<QuotaSummaryDto>.Success(new QuotaSummaryDto(
            quota.Id,
            quota.TenantId,
            quota.PayeeId,
            payee?.FullName ?? string.Empty,
            payee?.EmployeeCode ?? string.Empty,
            quota.PlanId,
            plan?.Name ?? string.Empty,
            quota.MeasurementType,
            quota.Amount.Amount,
            quota.Amount.Currency,
            quota.Period.Start,
            quota.Period.End,
            quota.Status.ToString(),
            quota.Notes,
            quota.CreatedAt));
    }
}
