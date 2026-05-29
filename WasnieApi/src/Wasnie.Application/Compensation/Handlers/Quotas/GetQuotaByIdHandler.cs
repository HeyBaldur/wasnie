using MediatR;
using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Compensation.DTOs;
using Wasnie.Application.Compensation.Queries.Quotas;
using Wasnie.Domain.Authorization;
using Wasnie.Domain.Common.Results;

namespace Wasnie.Application.Compensation.Handlers.Quotas;

public sealed class GetQuotaByIdHandler(IApplicationDbContext db, IAuthorizationService authorizationService)
    : IRequestHandler<GetQuotaByIdQuery, Result<QuotaSummaryDto>>
{
    public async Task<Result<QuotaSummaryDto>> Handle(GetQuotaByIdQuery request, CancellationToken cancellationToken)
    {
        await authorizationService.RequireAsync(Permission.QuotasRead, cancellationToken);
        var quota = await db.Quotas
            .FirstOrDefaultAsync(q => q.Id == request.QuotaId, cancellationToken);

        if (quota is null)
            return Result<QuotaSummaryDto>.Failure("Quota not found.");

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
