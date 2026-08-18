namespace Wasnie.Domain.Authorization;

public static class Permission
{
    public const string PayeesRead = "Payees.Read";
    public const string PayeesCreate = "Payees.Create";
    public const string PayeesUpdate = "Payees.Update";
    public const string PayeesTerminate = "Payees.Terminate";
    public const string PayeesDeactivate = "Payees.Deactivate";

    public const string PlansRead = "Plans.Read";
    public const string PlansCreate = "Plans.Create";
    public const string PlansUpdate = "Plans.Update";
    public const string PlansDelete = "Plans.Delete";
    public const string PlansActivate = "Plans.Activate";
    public const string PlansArchive = "Plans.Archive";

    public const string QuotasRead = "Quotas.Read";
    public const string QuotasSet = "Quotas.Set";
    public const string QuotasUpdate = "Quotas.Update";

    public const string AssignmentsRead = "Assignments.Read";
    public const string AssignmentsCreate = "Assignments.Create";
    public const string AssignmentsUpdate = "Assignments.Update";
    public const string AssignmentsDelete = "Assignments.Delete";

    public const string CreditsRead = "Credits.Read";
    public const string CreditsExport = "Credits.Export";
    public const string CreditsRecalculate = "Credits.Recalculate";

    public const string TransactionsCreate = "Transactions.Create";
    public const string TransactionsRead = "Transactions.Read";
    public const string TransactionsUpdate = "Transactions.Update";
    public const string TransactionsVoid = "Transactions.Void";
    public const string TransactionsProcessPending = "Transactions.ProcessPending";
    public const string TransactionsExport = "Transactions.Export";
    public const string TransactionsUpdateFromExcel = "Transactions.UpdateFromExcel";

    public const string PayoutsRead = "Payouts.Read";
    public const string PayoutsCalculate = "Payouts.Calculate";
    public const string PayoutsApprove = "Payouts.Approve";
    public const string PayoutsMarkPaid = "Payouts.MarkPaid";
    public const string PayoutsReopen = "Payouts.Reopen";
    public const string PayoutsExport = "Payouts.Export";
    public const string PayoutsDeleteDraft = "Payouts.DeleteDraft";

    // Clawback ledger. Read is deliberately broad — a rep seeing why their pay was reduced is the
    // point of the ledger, not a leak. Adjust writes a Human entry and is finance-only.
    public const string LedgerRead = "Ledger.Read";
    public const string LedgerAdjust = "Ledger.Adjust";

    /// <summary>
    /// The right to receive a FINISHED balance — earned, owed, net — and nothing else.
    ///
    /// ★ A FACADE PERMISSION, AND THE ALTERNATIVE IS WHY IT EXISTS. The balance summary has to cross the
    /// ledger with the payouts, or it reports a rep's 0.00 debt as their balance and tells them they are
    /// owed nothing (see PayeeLedgerSummaryDto). The obvious way to let a rep see their own balance is to
    /// grant them <see cref="PayoutsRead"/> and rely on the resource guard to funnel them — and that is
    /// the wrong trade: Payouts.Read opens the raw payout rows, the pay-run screens and the overlap
    /// queries, i.e. the whole payroll surface, and every one of those then needs its own filter to hold
    /// the line. Broad grant plus peripheral patching is a leak on a schedule.
    ///
    /// So the permission is scoped to the SHAPE OF THE ANSWER instead of to the tables behind it. The
    /// holder may receive a computed summary of one payee; they gain no access whatsoever to
    /// CompensationPayout, to a pay run, or to an export. The crossing happens inside the handler, which
    /// reads its sources with the application's own authority — the user never touches them.
    ///
    /// ★ IT AUTHORISES A SHAPE, NEVER A PERSON. Which payee a holder may summarise is a different
    /// question, answered by PayeeAccessGuard. Both apply, always: permission says WHAT you may receive,
    /// the guard says WHOSE.
    /// </summary>
    public const string LedgerSummaryRead = "LedgerSummary.Read";

    // Enrichment lookup table (product → category). Routes money by deciding a rule's category filter,
    // so managed by the same roles that edit plans.
    public const string CategoryMappingsRead = "CategoryMappings.Read";
    public const string CategoryMappingsManage = "CategoryMappings.Manage";

    public const string ImportsExecute = "Imports.Execute";
    public const string ReportsViewAll = "Reports.ViewAll";
    public const string SubscriptionManage = "Subscription.Manage";
    public const string SettingsUpdate = "Settings.Update";

    // Connecting a CRM exposes the tenant's data to a third party — restricted to tenant admins.
    public const string IntegrationsManage = "Integrations.Manage";
}
