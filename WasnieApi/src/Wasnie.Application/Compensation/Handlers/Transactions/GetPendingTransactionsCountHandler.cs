using MediatR;
using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Common.Abstractions;
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
        var assignment = await db.PlanAssignments
            .IgnoreQueryFilters()
            .Where(a => a.Id == assignmentId)
            .FirstOrDefaultAsync(ct);

        if (assignment is null || assignment.EffectivePeriod is null) return 0;

        var start = assignment.EffectivePeriod.Start;
        var end = assignment.EffectivePeriod.End;
        var payeeId = assignment.PayeeId;

        return await db.CompensationTransactions
            .Where(t => t.Status == CompensationTransactionStatus.Pending
                     && t.PayeeId == payeeId
                     && t.TransactionDate >= start
                     && t.TransactionDate <= end)
            .CountAsync(ct);
    }

    private static async Task<int> CountByPlan(IApplicationDbContext db, Guid planId, CancellationToken ct)
    {
        // Load full entities — EF Core owned-type (DateRange) cannot be projected in Select.
        var assignments = await db.PlanAssignments
            .IgnoreQueryFilters()
            .Where(a => a.PlanId == planId && a.Status == AssignmentStatus.Active)
            .ToListAsync(ct);

        if (assignments.Count == 0) return 0;

        // Union approach: count all Pending txns for any payee+period in this plan's assignments.
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
                         && t.TransactionDate <= end)
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
