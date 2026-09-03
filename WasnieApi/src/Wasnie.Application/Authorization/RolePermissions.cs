using Wasnie.Domain.Authorization;

namespace Wasnie.Application.Authorization;

public static class RolePermissions
{
    private static readonly IReadOnlySet<string> TenantAdminPermissions =
        new HashSet<string>(StringComparer.Ordinal)
        {
            Permission.PayeesRead, Permission.PayeesCreate, Permission.PayeesUpdate,
            Permission.PayeesTerminate, Permission.PayeesDeactivate,
            Permission.PlansRead, Permission.PlansCreate, Permission.PlansUpdate,
            Permission.PlansDelete, Permission.PlansActivate, Permission.PlansArchive,
            Permission.PlansStopRule,
            Permission.QuotasRead, Permission.QuotasSet, Permission.QuotasUpdate,
            Permission.AssignmentsRead, Permission.AssignmentsCreate, Permission.AssignmentsUpdate, Permission.AssignmentsDelete,
            Permission.TransactionsCreate, Permission.TransactionsRead, Permission.TransactionsUpdate,
            Permission.TransactionsProcessPending, Permission.TransactionsVoid,
            Permission.TransactionsExport, Permission.TransactionsUpdateFromExcel,
            Permission.CreditsRead, Permission.CreditsExport, Permission.CreditsRecalculate,
            Permission.PayoutsRead, Permission.PayoutsCalculate, Permission.PayoutsApprove,
            Permission.PayoutsMarkPaid, Permission.PayoutsReopen, Permission.PayoutsExport,
            Permission.PayoutsDeleteDraft,
            Permission.LedgerRead, Permission.LedgerAdjust, Permission.LedgerSummaryRead,
            Permission.LedgerCloseAccount,
            Permission.CategoryMappingsRead, Permission.CategoryMappingsManage,
            Permission.ImportsExecute, Permission.ReportsViewAll, Permission.SubscriptionManage,
            Permission.SettingsUpdate, Permission.IntegrationsManage,
        };

    private static readonly IReadOnlySet<string> CompManagerPermissions =
        new HashSet<string>(StringComparer.Ordinal)
        {
            Permission.PayeesRead, Permission.PayeesCreate, Permission.PayeesUpdate,
            Permission.PayeesTerminate, Permission.PayeesDeactivate,
            Permission.PlansRead, Permission.PlansCreate, Permission.PlansUpdate,
            Permission.PlansDelete, Permission.PlansActivate, Permission.PlansArchive,
            Permission.PlansStopRule,
            Permission.QuotasRead, Permission.QuotasSet, Permission.QuotasUpdate,
            Permission.AssignmentsRead, Permission.AssignmentsCreate, Permission.AssignmentsUpdate, Permission.AssignmentsDelete,
            Permission.TransactionsCreate, Permission.TransactionsRead, Permission.TransactionsUpdate,
            Permission.TransactionsProcessPending, Permission.TransactionsVoid,
            Permission.TransactionsExport, Permission.TransactionsUpdateFromExcel,
            Permission.CreditsRead, Permission.CreditsExport, Permission.CreditsRecalculate,
            Permission.PayoutsRead, Permission.PayoutsCalculate, Permission.PayoutsApprove,
            Permission.PayoutsMarkPaid, Permission.PayoutsReopen, Permission.PayoutsExport,
            Permission.PayoutsDeleteDraft,
            Permission.LedgerRead, Permission.LedgerAdjust, Permission.LedgerSummaryRead,
            Permission.LedgerCloseAccount,
            Permission.CategoryMappingsRead, Permission.CategoryMappingsManage,
            Permission.ImportsExecute, Permission.ReportsViewAll,
        };

    private static readonly IReadOnlySet<string> ManagerPermissions =
        new HashSet<string>(StringComparer.Ordinal)
        {
            Permission.PayeesRead,
            Permission.QuotasRead,
            Permission.AssignmentsRead,
            // A manager must be able to explain a reduced payment to their rep.
            Permission.LedgerRead,
            // ★ AND THE SUMMARY, WITHOUT Payouts.Read. Explaining a reduced payment needs the OTHER half
            // — what was earned — and the raw payouts surface is not the way to get it: this permission
            // buys a finished figure for one payee, not the payroll tables. Which reps a manager may
            // summarise is PayeeAccessGuard's business, not this list's.
            Permission.LedgerSummaryRead,
        };

    private static readonly IReadOnlySet<string> RepPermissions =
        new HashSet<string>(StringComparer.Ordinal)
        {
            Permission.PayeesRead,
            Permission.AssignmentsRead,
            Permission.QuotasRead,
            // Transparency is the differentiator: the rep sees their own balance and why it moved.
            Permission.LedgerRead,
            // ★ THE HALF THAT MAKES THAT SENTENCE TRUE. Ledger.Read alone shows only DEBT, so a rep who
            // owes nothing sees 0.00 and is told they have nothing coming — the false zero. This grants
            // the crossed figure. It does NOT grant Payouts.Read, and a rep must never receive it: the
            // raw payout rows, pay runs and exports are payroll's, not the sales floor's.
            Permission.LedgerSummaryRead,
        };

    private static readonly IReadOnlyDictionary<string, IReadOnlySet<string>> Map =
        new Dictionary<string, IReadOnlySet<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["TenantAdmin"] = TenantAdminPermissions,
            ["CompManager"] = CompManagerPermissions,
            ["Manager"] = ManagerPermissions,
            ["Rep"] = RepPermissions,
        };

    public static bool HasPermission(string roleName, string permission) =>
        Map.TryGetValue(roleName, out var perms) && perms.Contains(permission);

    public static IReadOnlySet<string> GetPermissions(string roleName) =>
        Map.TryGetValue(roleName, out var perms) ? perms : new HashSet<string>();
}
