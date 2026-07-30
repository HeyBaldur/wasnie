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

/// <summary>
/// The same quota for N payees, created all at once or not at all.
///
/// THREE PROPERTIES, and each one is load-bearing:
///
/// 1. ATOMIC. Every quota in the batch is validated BEFORE anything is written, and the writes go in
///    a single <c>SaveChangesAsync</c> — which EF executes inside one transaction. So a batch either
///    inserts all N rows or reaches SaveChanges never. There is no code path that writes some.
///    This is not politeness: the domain allows duplicate and overlapping quotas, so a partial
///    success would poison the retry. The admin fixes the two bad rows, re-sends the batch, and now
///    the eighteen good ones exist twice. All-or-nothing is what makes "correct and re-send" safe.
///
/// 2. DOMAIN PARITY. Not one business rule lives here. Every quota goes through
///    <see cref="QuotaBuilder.Build"/> — the exact code <see cref="CreateQuotaHandler"/> runs for a
///    single quota. A bulk endpoint that validated one rule less than the form it replaces would be a
///    way to inject data the form refuses; one rule MORE would be a rule that exists only in bulk,
///    which is the same disease in the other direction.
///
/// 3. SILENT ABOUT OVERLAPS. The domain currently permits a payee to hold overlapping quotas — the
///    engine resolves them by picking the narrowest period, which is what makes monthly quotas inside
///    a quarterly plan work. So this handler creates an overlapping quota without a word, exactly as
///    the single-create does. Warning here would be a business rule living in one endpoint. If
///    overlaps are ever forbidden, they get forbidden in <c>Quota</c>, for every path at once.
/// </summary>
public sealed class BulkCreateQuotasHandler(
    IApplicationDbContext db,
    ITenantContext tenantContext,
    ICurrentUserService currentUser,
    IClock clock,
    IGuidGenerator guid,
    IAuthorizationService authorizationService)
    : IRequestHandler<BulkCreateQuotasCommand, Result<BulkCreateQuotasResultDto>>
{
    public async Task<Result<BulkCreateQuotasResultDto>> Handle(
        BulkCreateQuotasCommand request, CancellationToken cancellationToken)
    {
        // Same permission as creating one. Creating twenty is not a different authority.
        await authorizationService.RequireAsync(Permission.QuotasSet, cancellationToken);

        var plan = await db.CompensationPlans
            .FirstOrDefaultAsync(p => p.Id == request.PlanId, cancellationToken);
        if (plan is null)
            return Result<BulkCreateQuotasResultDto>.Failure("Plan not found.");

        // Built once: they are identical for every payee, and building them per payee would be the
        // first step towards the two paths diverging.
        var amount = Money.OfNonNegative(request.Amount, request.Currency);
        var period = DateRange.Of(request.PeriodStart, request.PeriodEnd);

        // Names for the failure report ONLY — never a validation. The single-create does not check
        // that the payee exists (it writes the quota and leaves the name blank on the response), so
        // checking it here would be a bulk-only rule. An id matching nobody comes back with an empty
        // name in the report, which is the signal without being a new gate.
        var payees = await db.Payees
            .Where(p => request.PayeeIds.Contains(p.Id))
            .Select(p => new { p.Id, p.FullName, p.EmployeeCode })
            .ToListAsync(cancellationToken);

        var payeeById = payees.ToDictionary(p => p.Id);

        var now = clock.UtcNowOffset;
        var actor = currentUser.UserId ?? "system";

        var built = new List<Quota>(request.PayeeIds.Count);
        var failures = new List<BulkQuotaFailureDto>();

        foreach (var payeeId in request.PayeeIds)
        {
            var result = QuotaBuilder.Build(
                plan, tenantContext.TenantId, payeeId, amount, period,
                request.MeasurementType, request.Notes, actor, guid.NewGuid(), now);

            if (result.IsSuccess)
            {
                built.Add(result.Value!);
                continue;
            }

            payeeById.TryGetValue(payeeId, out var payee);
            failures.Add(new BulkQuotaFailureDto(
                payeeId, payee?.FullName ?? string.Empty, payee?.EmployeeCode ?? string.Empty, result.Error!));
        }

        // The whole batch is refused on the first failure — and refused BEFORE the write, so there is
        // nothing to roll back. Returning here with `built` non-empty is the atomicity guarantee:
        // those quotas were only ever objects in memory.
        if (failures.Count > 0)
            return Result<BulkCreateQuotasResultDto>.Success(
                new BulkCreateQuotasResultDto([], failures));

        db.Quotas.AddRange(built);
        await db.SaveChangesAsync(cancellationToken);

        var created = built
            .Select(q =>
            {
                payeeById.TryGetValue(q.PayeeId, out var payee);
                return new QuotaSummaryDto(
                    q.Id,
                    q.TenantId,
                    q.PayeeId,
                    payee?.FullName ?? string.Empty,
                    payee?.EmployeeCode ?? string.Empty,
                    q.PlanId,
                    plan.Name,
                    q.MeasurementType,
                    q.Amount.Amount,
                    q.Amount.Currency,
                    q.Period.Start,
                    q.Period.End,
                    q.Status.ToString(),
                    q.Notes,
                    q.CreatedAt);
            })
            .ToList();

        return Result<BulkCreateQuotasResultDto>.Success(
            new BulkCreateQuotasResultDto(created, []));
    }
}
