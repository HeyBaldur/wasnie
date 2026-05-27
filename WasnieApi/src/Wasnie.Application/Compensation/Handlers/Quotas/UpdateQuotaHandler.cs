using MediatR;
using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Common.Abstractions;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Compensation.Commands.Quotas;
using Wasnie.Application.Compensation.DTOs;
using Wasnie.Domain.Common.Results;
using Wasnie.Domain.Compensation.ValueObjects;

namespace Wasnie.Application.Compensation.Handlers.Quotas;

public sealed class UpdateQuotaHandler(IApplicationDbContext db, ICurrentUserService currentUser, IClock clock)
    : IRequestHandler<UpdateQuotaCommand, Result<QuotaSummaryDto>>
{
    public async Task<Result<QuotaSummaryDto>> Handle(UpdateQuotaCommand request, CancellationToken cancellationToken)
    {
        var quota = await db.Quotas.FirstOrDefaultAsync(q => q.Id == request.QuotaId, cancellationToken);
        if (quota is null)
            return Result<QuotaSummaryDto>.Failure("Quota not found.");

        try
        {
            quota.UpdateDraft(
                Money.OfNonNegative(request.Amount, request.Currency),
                DateRange.Of(request.PeriodStart, request.PeriodEnd),
                request.MeasurementType,
                request.Notes,
                currentUser.UserId ?? "system",
                clock.UtcNowOffset);
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
