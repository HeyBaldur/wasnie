using MediatR;
using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Common.Abstractions;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Compensation.Commands.Quotas;
using Wasnie.Application.Compensation.DTOs;
using Wasnie.Domain.Authorization;
using Wasnie.Domain.Common.Results;
using Wasnie.Domain.Compensation.Quotas;
using Wasnie.Domain.Compensation.ValueObjects;

namespace Wasnie.Application.Compensation.Handlers.Quotas;

public sealed class CreateQuotaHandler(
    IApplicationDbContext db,
    ITenantContext tenantContext,
    ICurrentUserService currentUser,
    IClock clock,
    IGuidGenerator guid,
    IAuthorizationService authorizationService)
    : IRequestHandler<CreateQuotaCommand, Result<QuotaSummaryDto>>
{
    public async Task<Result<QuotaSummaryDto>> Handle(CreateQuotaCommand request, CancellationToken cancellationToken)
    {
        await authorizationService.RequireAsync(Permission.QuotasSet, cancellationToken);
        var amount = Money.OfNonNegative(request.Amount, request.Currency);
        var period = DateRange.Of(request.PeriodStart, request.PeriodEnd);

        var quota = Quota.Create(
            tenantContext.TenantId,
            request.PayeeId,
            request.PlanId,
            amount,
            period,
            request.MeasurementType,
            currentUser.UserId ?? "system",
            guid.NewGuid(),
            clock.UtcNowOffset,
            request.Notes);

        db.Quotas.Add(quota);
        await db.SaveChangesAsync(cancellationToken);

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
