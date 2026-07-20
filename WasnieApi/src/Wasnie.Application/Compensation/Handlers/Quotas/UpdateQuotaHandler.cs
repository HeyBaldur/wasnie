using MediatR;
using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Common.Abstractions;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Compensation.Commands.Quotas;
using Wasnie.Application.Compensation.Common;
using Wasnie.Application.Compensation.DTOs;
using Wasnie.Domain.Authorization;
using Wasnie.Domain.Common.Results;
using Wasnie.Domain.Compensation.ValueObjects;

namespace Wasnie.Application.Compensation.Handlers.Quotas;

public sealed class UpdateQuotaHandler(
    IApplicationDbContext db,
    ICurrentUserService currentUser,
    IClock clock,
    IAuthorizationService authorizationService)
    : IRequestHandler<UpdateQuotaCommand, Result<QuotaSummaryDto>>
{
    public async Task<Result<QuotaSummaryDto>> Handle(UpdateQuotaCommand request, CancellationToken cancellationToken)
    {
        await authorizationService.RequireAsync(Permission.QuotasUpdate, cancellationToken);
        var quota = await db.Quotas.FirstOrDefaultAsync(q => q.Id == request.QuotaId, cancellationToken);
        if (quota is null)
            return Result<QuotaSummaryDto>.Failure("Quota not found.");

        var plan = await db.CompensationPlans.FirstOrDefaultAsync(p => p.Id == quota.PlanId, cancellationToken);

        // Integrity guard (same rule as CreateQuotaHandler): the updated quota period must stay
        // CONTAINED within the plan's effective period. The create flow already enforces this; the
        // update endpoint is the back door for the same misalignment, so it must reject too. The
        // backend is the source of truth even though the UI validates client-side.
        if (plan is not null)
        {
            var periodError = QuotaPeriodGuard.Validate(plan.EffectivePeriod, request.PeriodStart, request.PeriodEnd);
            if (periodError is not null)
                return Result<QuotaSummaryDto>.Failure(periodError);
        }

        try
        {
            quota.UpdateDraft(
                Money.OfNonNegative(request.Amount, request.Currency),
                DateRange.Of(request.PeriodStart, request.PeriodEnd),
                request.MeasurementType,
                request.Notes,
                currentUser.UserId ?? "system",
                clock.UtcNowOffset,
                planCurrency: plan?.Currency);
        }
        catch (Exception ex)
        {
            return Result<QuotaSummaryDto>.Failure(ex.Message);
        }

        await db.SaveChangesAsync(cancellationToken);

        var payee = await db.Payees.FirstOrDefaultAsync(p => p.Id == quota.PayeeId, cancellationToken);

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
