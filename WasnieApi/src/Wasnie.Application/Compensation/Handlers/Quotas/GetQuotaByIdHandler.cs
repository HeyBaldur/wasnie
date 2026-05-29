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

        var payeeTask = db.Payees.FirstOrDefaultAsync(p => p.Id == quota.PayeeId, cancellationToken);
        var planTask = db.CompensationPlans.FirstOrDefaultAsync(p => p.Id == quota.PlanId, cancellationToken);
        await Task.WhenAll(payeeTask, planTask);

        return Result<QuotaSummaryDto>.Success(new QuotaSummaryDto(
            quota.Id,
            quota.TenantId,
            quota.PayeeId,
            payeeTask.Result?.FullName ?? string.Empty,
            payeeTask.Result?.EmployeeCode ?? string.Empty,
            quota.PlanId,
            planTask.Result?.Name ?? string.Empty,
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
