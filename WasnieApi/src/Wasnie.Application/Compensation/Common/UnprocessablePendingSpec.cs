using Wasnie.Application.Common.Interfaces;
using Wasnie.Domain.Compensation.Assignments;
using Wasnie.Domain.Compensation.Enums;
using Wasnie.Domain.Compensation.Transactions;

namespace Wasnie.Application.Compensation.Common;

/// <summary>
/// Single source of truth for "which Pending transactions cannot be processed yet, and why".
///
/// A Pending transaction is PROCESSABLE iff it has a payee, that payee has an Active assignment whose
/// EffectivePeriod covers the transaction date, AND that plan's currency equals the transaction currency
/// (the exact rule the engine enforces in ProcessPendingTransactionsJobHandler). These queryables select
/// the Pending transactions that FAIL that test, split into mutually-exclusive primary reasons.
///
/// Both the dashboard "needs attention" card (counts) and the Transactions list ("attentionReason" filter)
/// use these SAME queryables, so the dashboard count and the filtered list always agree. Composed entirely
/// as IQueryable (EXISTS subqueries) so the list stays server-paginated — no in-memory materialization.
/// All DbSets carry the tenant query filter (Rule 9).
/// </summary>
public static class UnprocessablePendingSpec
{
    public const string NoPayeeReason = "NoPayee";
    public const string CurrencyMismatchReason = "CurrencyMismatch";
    public const string NoActiveAssignmentReason = "NoActiveAssignment";

    /// <summary>Pending, no payee at all → must be assigned.</summary>
    public static IQueryable<CompensationTransaction> NoPayee(IApplicationDbContext db) =>
        db.CompensationTransactions.Where(t =>
            t.Status == CompensationTransactionStatus.Pending && t.PayeeId == null);

    /// <summary>Pending, has a payee, but NO Active assignment covers the transaction date.</summary>
    public static IQueryable<CompensationTransaction> NoActiveAssignment(IApplicationDbContext db) =>
        db.CompensationTransactions.Where(t =>
            t.Status == CompensationTransactionStatus.Pending
            && t.PayeeId != null
            && !db.PlanAssignments.Any(a =>
                a.Status == AssignmentStatus.Active
                && a.PayeeId == t.PayeeId
                && a.EffectivePeriod.Start <= t.TransactionDate
                && a.EffectivePeriod.End >= t.TransactionDate));

    /// <summary>
    /// Pending, has a payee, a covering Active assignment exists, but NONE of those covering plans is in
    /// the transaction's currency (e.g. USD transaction, EUR plan).
    /// </summary>
    public static IQueryable<CompensationTransaction> CurrencyMismatch(IApplicationDbContext db) =>
        db.CompensationTransactions.Where(t =>
            t.Status == CompensationTransactionStatus.Pending
            && t.PayeeId != null
            && db.PlanAssignments.Any(a =>
                a.Status == AssignmentStatus.Active
                && a.PayeeId == t.PayeeId
                && a.EffectivePeriod.Start <= t.TransactionDate
                && a.EffectivePeriod.End >= t.TransactionDate)
            && !(from a in db.PlanAssignments
                 join pl in db.CompensationPlans on a.PlanId equals pl.Id
                 where a.Status == AssignmentStatus.Active
                     && a.PayeeId == t.PayeeId
                     && a.EffectivePeriod.Start <= t.TransactionDate
                     && a.EffectivePeriod.End >= t.TransactionDate
                     && pl.Currency == t.Amount.Currency
                 select 1).Any());

    /// <summary>Resolves the queryable for a reason string, or null if the reason is unknown.</summary>
    public static IQueryable<CompensationTransaction>? ForReason(IApplicationDbContext db, string? reason) =>
        reason switch
        {
            NoPayeeReason => NoPayee(db),
            CurrencyMismatchReason => CurrencyMismatch(db),
            NoActiveAssignmentReason => NoActiveAssignment(db),
            _ => null,
        };
}
