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

    public const string TransactionsCreate = "Transactions.Create";
    public const string TransactionsRead = "Transactions.Read";
    public const string TransactionsUpdate = "Transactions.Update";
    public const string TransactionsProcessPending = "Transactions.ProcessPending";
    public const string TransactionsExport = "Transactions.Export";
    public const string TransactionsUpdateFromExcel = "Transactions.UpdateFromExcel";

    public const string ImportsExecute = "Imports.Execute";
    public const string ReportsViewAll = "Reports.ViewAll";
    public const string SubscriptionManage = "Subscription.Manage";
    public const string SettingsUpdate = "Settings.Update";
}
