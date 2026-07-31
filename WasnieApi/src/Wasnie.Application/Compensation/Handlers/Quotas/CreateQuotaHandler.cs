using MediatR;
using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Common.Abstractions;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Compensation.Commands.Quotas;
using Wasnie.Application.Compensation.Common;
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

        var amount = Money.OfNonNegative(request.Amount, request.Currency);
        var period = DateRange.Of(request.PeriodStart, request.PeriodEnd);

        // Validation and construction live in QuotaBuilder, shared with the bulk-create handler so the
        // two paths cannot drift apart. The rules did not change when they moved there.
        var built = QuotaBuilder.Build(
            plan,
            tenantContext.TenantId,
            request.PayeeId,
            amount,
            period,
            request.MeasurementType,
            request.Notes,
            currentUser.UserId ?? "system",
            guid.NewGuid(),
            clock.UtcNowOffset);

        if (!built.IsSuccess)
            return Result<QuotaSummaryDto>.Failure(built.Error!);

        var quota = built.Value!;

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
