using MediatR;
using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Compensation.Calculation;
using Wasnie.Application.Compensation.Queries.Transactions;
using Wasnie.Domain.Authorization;
using Wasnie.Domain.Common.Results;
using Wasnie.Domain.Compensation.Enums;

namespace Wasnie.Application.Compensation.Handlers.Transactions;

public sealed class GetPendingTransactionsCountHandler(
    IApplicationDbContext db,
    IAuthorizationService authorizationService)
    : IRequestHandler<GetPendingTransactionsCountQuery, Result<int>>
{
    public async Task<Result<int>> Handle(GetPendingTransactionsCountQuery request, CancellationToken cancellationToken)
    {
        await authorizationService.RequireAsync(Permission.TransactionsProcessPending, cancellationToken);

        var count = await CountCandidatesAsync(db, request.Scope, request.ScopeId,
            request.PeriodStart, request.PeriodEnd, cancellationToken);

        return Result<int>.Success(count);
    }

    internal static async Task<int> CountCandidatesAsync(
        IApplicationDbContext db,
        ProcessPendingScope scope,
        Guid? scopeId,
        DateOnly? periodStart,
        DateOnly? periodEnd,
        CancellationToken ct)
    {
        return scope switch
        {
            ProcessPendingScope.ByPlanAssignment => await CountByAssignment(db, scopeId!.Value, ct),
            ProcessPendingScope.ByPlan => await CountByPlan(db, scopeId!.Value, ct),
            ProcessPendingScope.ByPayeeAndPeriod => await CountByPayeeAndPeriod(db, scopeId!.Value, periodStart!.Value, periodEnd!.Value, ct),
            _ => 0
        };
    }

    private static async Task<int> CountByAssignment(IApplicationDbContext db, Guid assignmentId, CancellationToken ct)
    {
        // Load full entity — EF Core owned-type (DateRange) cannot be projected in Select.
        // ★★ NO IgnoreQueryFilters HERE, AND ITS ABSENCE IS THE SECURITY CONTROL (KAN-21). It used
        //    to be present, which lifted the tenant filter off this read: an assignment or plan id
        //    belonging to ANOTHER tenant resolved, and the endpoint answered 200 with a count
        //    instead of refusing. No foreign rows ever crossed — the transaction queries below keep
        //    the caller's filter — but it was an existence oracle for another tenant's ids.
        //
        // ★ THE FILTER IS ENOUGH BECAUSE EVERY CALLER IS AN HTTP REQUEST (the transactions
        //   controller and ProcessPendingTransactions), so the ambient tenant is always set. A
        //   background caller would need the explicit `TenantId ==` form instead — never the
        //   unfiltered read that was here.
        var assignment = await db.PlanAssignments
            .Where(a => a.Id == assignmentId)
            .FirstOrDefaultAsync(ct);

        if (assignment is null || assignment.EffectivePeriod is null) return 0;

        // Load the plan currency: only transactions in this currency are eligible.
        var plan = await db.CompensationPlans
            .Where(p => p.Id == assignment.PlanId)
            .Select(p => new { p.Currency })
            .FirstOrDefaultAsync(ct);

        if (plan is null) return 0;

        var start = assignment.EffectivePeriod.Start;
        var end = assignment.EffectivePeriod.End;
        var payeeId = assignment.PayeeId;

        return await db.CompensationTransactions
            .Where(t => t.Status == CompensationTransactionStatus.Pending
                     && t.PayeeId == payeeId
                     && t.TransactionDate >= start
                     && t.TransactionDate <= end
                     && t.Amount.Currency == plan.Currency)
            .CountAsync(ct);
    }

    private static async Task<int> CountByPlan(IApplicationDbContext db, Guid planId, CancellationToken ct)
    {
        // Load the plan currency: only transactions in this currency are eligible.
        var plan = await db.CompensationPlans
            .Where(p => p.Id == planId)
            .Select(p => new { p.Currency })
            .FirstOrDefaultAsync(ct);

        if (plan is null) return 0;

        // Load full entities — EF Core owned-type (DateRange) cannot be projected in Select.
        var assignments = await db.PlanAssignments
            .Where(a => a.PlanId == planId && a.Status == AssignmentStatus.Active)
            .ToListAsync(ct);

        if (assignments.Count == 0) return 0;

        var total = 0;
        foreach (var a in assignments.Where(a => a.EffectivePeriod is not null))
        {
            var start = a.EffectivePeriod!.Start;
            var end = a.EffectivePeriod.End;
            var payeeId = a.PayeeId;

            total += await db.CompensationTransactions
                .Where(t => t.Status == CompensationTransactionStatus.Pending
                         && t.PayeeId == payeeId
                         && t.TransactionDate >= start
                         && t.TransactionDate <= end
                         && t.Amount.Currency == plan.Currency)
                .CountAsync(ct);
        }

        return total;
    }

    private static async Task<int> CountByPayeeAndPeriod(
        IApplicationDbContext db, Guid payeeId, DateOnly periodStart, DateOnly periodEnd, CancellationToken ct)
    {
        return await db.CompensationTransactions
            .Where(t => t.Status == CompensationTransactionStatus.Pending
                     && t.PayeeId == payeeId
                     && t.TransactionDate >= periodStart
                     && t.TransactionDate <= periodEnd)
            .CountAsync(ct);
    }
}
