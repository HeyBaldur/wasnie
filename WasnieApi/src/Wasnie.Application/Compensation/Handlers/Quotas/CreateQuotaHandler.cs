using MediatR;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Compensation.Commands.Quotas;
using Wasnie.Application.Compensation.DTOs;
using Wasnie.Application.Compensation.Mappings;
using Wasnie.Domain.Common.Results;
using Wasnie.Domain.Compensation.Quotas;
using Wasnie.Domain.Compensation.ValueObjects;

namespace Wasnie.Application.Compensation.Handlers.Quotas;

public sealed class CreateQuotaHandler(
    IApplicationDbContext db,
    ITenantContext tenantContext,
    ICurrentUserService currentUser)
    : IRequestHandler<CreateQuotaCommand, Result<QuotaDto>>
{
    public async Task<Result<QuotaDto>> Handle(CreateQuotaCommand request, CancellationToken cancellationToken)
    {
        var amount = Money.OfNonNegative(request.Amount, request.Currency);
        var period = DateRange.Of(request.PeriodStart, request.PeriodEnd);

        var quota = Quota.Create(
            tenantContext.TenantId,
            request.PayeeId,
            request.PlanId,
            amount,
            period,
            currentUser.UserId ?? "system");

        db.Quotas.Add(quota);
        await db.SaveChangesAsync(cancellationToken);

        return Result<QuotaDto>.Success(CompensationMapper.ToQuotaDto(quota));
    }
}
