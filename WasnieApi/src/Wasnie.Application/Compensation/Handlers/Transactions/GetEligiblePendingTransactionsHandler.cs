using MediatR;
using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Compensation.Calculation;
using Wasnie.Application.Compensation.Queries.Transactions;
using Wasnie.Domain.Authorization;
using Wasnie.Domain.Common.Results;
using Wasnie.Domain.Compensation.Enums;
using Wasnie.Domain.Compensation.Transactions;

namespace Wasnie.Application.Compensation.Handlers.Transactions;

public sealed class GetEligiblePendingTransactionsHandler(
    IApplicationDbContext db,
    IAuthorizationService authorizationService)
    : IRequestHandler<GetEligiblePendingTransactionsQuery, Result<EligiblePendingResult>>
{
    private const int MaxInlineRows = 200;

    public async Task<Result<EligiblePendingResult>> Handle(
        GetEligiblePendingTransactionsQuery request, CancellationToken cancellationToken)
    {
        await authorizationService.RequireAsync(Permission.TransactionsProcessPending, cancellationToken);

        var (transactions, totalCount) = request.Scope switch
        {
            ProcessPendingScope.ByPlanAssignment => await LoadByAssignmentAsync(
                db, request.ScopeId!.Value, cancellationToken),

            ProcessPendingScope.ByPlan => await LoadByPlanAsync(
                db, request.ScopeId!.Value, cancellationToken),

            ProcessPendingScope.ByPayeeAndPeriod => await LoadByPayeeAndPeriodAsync(
                db, request.ScopeId!.Value, request.PeriodStart!.Value, request.PeriodEnd!.Value, cancellationToken),

            _ => ((IReadOnlyList<CompensationTransaction>)Array.Empty<CompensationTransaction>(), 0)
        };

        var payeeIds = transactions
            .Where(t => t.PayeeId.HasValue)
            .Select(t => t.PayeeId!.Value)
            .Distinct()
            .ToList();

        var payeeLookup = payeeIds.Count > 0
            ? await db.Payees
                .Where(p => payeeIds.Contains(p.Id))
                .Select(p => new PayeeSummary(p.Id, p.FullName, p.EmployeeCode))
                .ToDictionaryAsync(p => p.Id, cancellationToken)
            : new Dictionary<Guid, PayeeSummary>();

        var dtos = transactions.Select(t =>
        {
            payeeLookup.TryGetValue(t.PayeeId ?? Guid.Empty, out var payee);
            return new EligiblePendingTransactionDto(
                Id: t.Id,
                PayeeId: t.PayeeId,
                ReferenceNumber: t.ReferenceNumber,
                PayeeName: payee?.FullName,
                PayeeCode: payee?.EmployeeCode,
                TransactionDate: t.TransactionDate,
                Amount: t.Amount.Amount,
                Currency: t.Amount.Currency);
        }).ToList();

        return Result<EligiblePendingResult>.Success(new EligiblePendingResult(dtos, totalCount));
    }

    // Identical predicate to GetPendingTransactionsCountHandler.CountByAssignment (Pattern B).
    private static async Task<(IReadOnlyList<CompensationTransaction> Transactions, int TotalCount)>
        LoadByAssignmentAsync(IApplicationDbContext db, Guid assignmentId, CancellationToken ct)
    {
        var assignment = await db.PlanAssignments
            .IgnoreQueryFilters()
            .Where(a => a.Id == assignmentId)
            .FirstOrDefaultAsync(ct);

        if (assignment is null || assignment.EffectivePeriod is null)
            return (Array.Empty<CompensationTransaction>(), 0);

        // Pattern B: load plan currency — only show transactions that will actually process.
        var plan = await db.CompensationPlans
            .IgnoreQueryFilters()
            .Where(p => p.Id == assignment.PlanId)
            .Select(p => new { p.Currency })
            .FirstOrDefaultAsync(ct);

        if (plan is null)
            return (Array.Empty<CompensationTransaction>(), 0);

        var payeeId = assignment.PayeeId;
        var start = assignment.EffectivePeriod.Start;
        var end = assignment.EffectivePeriod.End;

        var totalCount = await db.CompensationTransactions
            .Where(t => t.Status == CompensationTransactionStatus.Pending
                     && t.PayeeId == payeeId
                     && t.TransactionDate >= start
                     && t.TransactionDate <= end
                     && t.Amount.Currency == plan.Currency)
            .CountAsync(ct);

        var rows = await db.CompensationTransactions
            .Where(t => t.Status == CompensationTransactionStatus.Pending
                     && t.PayeeId == payeeId
                     && t.TransactionDate >= start
                     && t.TransactionDate <= end
                     && t.Amount.Currency == plan.Currency)
            .OrderBy(t => t.TransactionDate)
            .Take(MaxInlineRows)
            .ToListAsync(ct);

        return (rows, totalCount);
    }

    // Same predicate as GetPendingTransactionsCountHandler.CountByPlan (Pattern B).
    // Uses 2 queries total (not N+1 per assignment) per the forbidden-patterns batch rule.
    private static async Task<(IReadOnlyList<CompensationTransaction> Transactions, int TotalCount)>
        LoadByPlanAsync(IApplicationDbContext db, Guid planId, CancellationToken ct)
    {
        // Pattern B: load plan currency — only show transactions that will actually process.
        var plan = await db.CompensationPlans
            .IgnoreQueryFilters()
            .Where(p => p.Id == planId)
            .Select(p => new { p.Currency })
            .FirstOrDefaultAsync(ct);

        if (plan is null)
            return (Array.Empty<CompensationTransaction>(), 0);

        var assignments = await db.PlanAssignments
            .IgnoreQueryFilters()
            .Where(a => a.PlanId == planId && a.Status == AssignmentStatus.Active)
            .ToListAsync(ct);

        var eligibleAssignments = assignments
            .Where(a => a.EffectivePeriod is not null)
            .ToList();

        if (eligibleAssignments.Count == 0)
            return (Array.Empty<CompensationTransaction>(), 0);

        var allPayeeIds = eligibleAssignments.Select(a => a.PayeeId).Distinct().ToList();

        // Single query: all Pending transactions for plan's payees, in the plan's currency.
        // EF Core 8 cannot translate DateOnly comparisons on the owned DateRange type in WHERE.
        var candidates = await db.CompensationTransactions
            .Where(t => t.Status == CompensationTransactionStatus.Pending
                     && t.PayeeId.HasValue
                     && allPayeeIds.Contains(t.PayeeId!.Value)
                     && t.Amount.Currency == plan.Currency)
            .OrderBy(t => t.TransactionDate)
            .ToListAsync(ct);

        var matched = candidates.Where(t =>
        {
            if (!t.PayeeId.HasValue) return false;
            return eligibleAssignments.Any(a =>
                a.PayeeId == t.PayeeId.Value
                && t.TransactionDate >= a.EffectivePeriod!.Start
                && t.TransactionDate <= a.EffectivePeriod.End);
        }).ToList();

        return (matched.Take(MaxInlineRows).ToList(), matched.Count);
    }

    // Identical predicate to GetPendingTransactionsCountHandler.CountByPayeeAndPeriod.
    private static async Task<(IReadOnlyList<CompensationTransaction> Transactions, int TotalCount)>
        LoadByPayeeAndPeriodAsync(
            IApplicationDbContext db, Guid payeeId, DateOnly periodStart, DateOnly periodEnd, CancellationToken ct)
    {
        var totalCount = await db.CompensationTransactions
            .Where(t => t.Status == CompensationTransactionStatus.Pending
                     && t.PayeeId == payeeId
                     && t.TransactionDate >= periodStart
                     && t.TransactionDate <= periodEnd)
            .CountAsync(ct);

        var rows = await db.CompensationTransactions
            .Where(t => t.Status == CompensationTransactionStatus.Pending
                     && t.PayeeId == payeeId
                     && t.TransactionDate >= periodStart
                     && t.TransactionDate <= periodEnd)
            .OrderBy(t => t.TransactionDate)
            .Take(MaxInlineRows)
            .ToListAsync(ct);

        return (rows, totalCount);
    }

    private sealed record PayeeSummary(Guid Id, string FullName, string? EmployeeCode);
}
