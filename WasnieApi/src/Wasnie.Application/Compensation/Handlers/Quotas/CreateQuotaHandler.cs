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

        var plan = await db.CompensationPlans.FirstOrDefaultAsync(p => p.Id == request.PlanId, cancellationToken);
        if (plan is null)
            return Result<QuotaSummaryDto>.Failure("Plan not found.");

        // Integrity guard: the quota period must fall within the plan's effective period.
        // The UI defaults to the plan period and validates this client-side, but a direct API
        // call must not be able to set a window outside the plan that attainment is measured against.
        if (request.PeriodStart < plan.EffectivePeriod.Start ||
            request.PeriodEnd > plan.EffectivePeriod.End)
            return Result<QuotaSummaryDto>.Failure(
                "The quota period must fall within the selected plan's effective period.");

        var amount = Money.OfNonNegative(request.Amount, request.Currency);
        var period = DateRange.Of(request.PeriodStart, request.PeriodEnd);

        Quota quota;
        try
        {
            quota = Quota.Create(
                tenantContext.TenantId,
                request.PayeeId,
                request.PlanId,
                amount,
                period,
                request.MeasurementType,
                currentUser.UserId ?? "system",
                guid.NewGuid(),
                clock.UtcNowOffset,
                request.Notes,
                planCurrency: plan.Currency);
        }
        catch (Exception ex)
        {
            return Result<QuotaSummaryDto>.Failure(ex.Message);
        }

        db.Quotas.Add(quota);
        await db.SaveChangesAsync(cancellationToken);

        var payee = await db.Payees.FirstOrDefaultAsync(p => p.Id == quota.PayeeId, cancellationToken);

        return Result<QuotaSummaryDto>.Success(new QuotaSummaryDto(
            quota.Id,
            quota.TenantId,
            quota.PayeeId,
            payee?.FullName ?? string.Empty,
            payee?.EmployeeCode ?? string.Empty,
            quota.PlanId,
            plan.Name,
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
