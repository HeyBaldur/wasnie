using MediatR;
using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Common.Abstractions;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Compensation.Calculation;
using Wasnie.Domain.Common.Results;
using Wasnie.Application.Compensation.DTOs;
using Wasnie.Application.Compensation.Queries.Quotas;
using Wasnie.Domain.Authorization;
using Wasnie.Domain.Compensation.Enums;
using Wasnie.Domain.Compensation.ValueObjects;

namespace Wasnie.Application.Compensation.Handlers.Quotas;

public sealed class GetPayeeAttainmentHandler(
    IApplicationDbContext db,
    IAuthorizationService authorizationService,
    IPayeeAccessGuard payeeAccessGuard,
    IClock clock)
    : IRequestHandler<GetPayeeAttainmentQuery, Result<IReadOnlyList<QuotaAttainmentDto>>>
{
    public async Task<Result<IReadOnlyList<QuotaAttainmentDto>>> Handle(
        GetPayeeAttainmentQuery request, CancellationToken cancellationToken)
    {
        await authorizationService.RequireAsync(Permission.QuotasRead, cancellationToken);

        // The most sensitive of the quota endpoints: target AND achieved AND the percentage — a
        // performance review of a named person, computed. Same empty answer an unknown payee gets.
        if (!await payeeAccessGuard.CanReadAsync(request.PayeeId, cancellationToken))
            return Result<IReadOnlyList<QuotaAttainmentDto>>.Success(Array.Empty<QuotaAttainmentDto>());

        var today = DateOnly.FromDateTime(clock.UtcNow);

        // Load all non-Draft quotas for this payee, ordered most-recent first.
        var quotas = await db.Quotas
            .Where(q => q.PayeeId == request.PayeeId && q.Status != QuotaStatus.Draft)
            .OrderByDescending(q => q.Period.Start)
            .ToListAsync(cancellationToken);

        if (quotas.Count == 0)
            return Result<IReadOnlyList<QuotaAttainmentDto>>.Success(
                Array.Empty<QuotaAttainmentDto>());

        // Load plan names + currencies in one query.
        var planIds = quotas.Select(q => q.PlanId).Distinct().ToList();
        var planInfoById = await db.CompensationPlans
            .Where(p => planIds.Contains(p.Id))
            .Select(p => new { p.Id, p.Name, p.Currency })
            .ToDictionaryAsync(p => p.Id, p => (p.Name, p.Currency), cancellationToken);

        var dtos = new List<QuotaAttainmentDto>(quotas.Count);

        foreach (var quota in quotas)
        {
            var achieved = await ComputeAchievedAsync(request.PayeeId, quota, cancellationToken);
            var attainment = AttainmentPercentage.FromAchievedAndTarget(achieved, quota.Amount.Amount);
            var planInfo = planInfoById.GetValueOrDefault(quota.PlanId, (Name: string.Empty, Currency: string.Empty));
            var isCurrencyValid = string.Equals(quota.Amount.Currency, planInfo.Currency, StringComparison.OrdinalIgnoreCase);

            dtos.Add(new QuotaAttainmentDto(
                QuotaId: quota.Id,
                PlanId: quota.PlanId,
                PlanName: planInfo.Name,
                MeasurementType: quota.MeasurementType,
                TargetAmount: quota.Amount.Amount,
                Currency: quota.Amount.Currency,
                AchievedAmount: achieved,
                AttainmentValue: attainment.Value,
                AttainmentPercent: attainment.ToPercentString(),
                PeriodStart: quota.Period.Start,
                PeriodEnd: quota.Period.End,
                Status: quota.Status.ToString(),
                IsCurrencyValid: isCurrencyValid,
                PlanCurrency: planInfo.Currency));
        }

        return Result<IReadOnlyList<QuotaAttainmentDto>>.Success(dtos);
    }

    // Deduped achieved via the shared QuotaAchievedQuery — same source of truth as the motor, so this
    // endpoint can't drift back to the per-credit double count.
    private Task<decimal> ComputeAchievedAsync(
        Guid payeeId,
        Wasnie.Domain.Compensation.Quotas.Quota quota,
        CancellationToken ct)
        => quota.MeasurementType == QuotaMeasurementType.Units
            ? QuotaAchievedQuery.UnitsAsync(db, payeeId, quota.PlanId, quota.Period.Start, quota.Period.End, ct)
            : QuotaAchievedQuery.RevenueAsync(db, payeeId, quota.PlanId, quota.Period.Start, quota.Period.End, quota.Amount.Currency, ct);
}
