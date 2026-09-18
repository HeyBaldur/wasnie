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
            Permission.PayoutsDeleteDraft, Permission.PayoutsDiscard,
            Permission.LedgerRead, Permission.LedgerAdjust, Permission.LedgerSummaryRead,
            Permission.LedgerCloseAccount,
            // KAN-19. Deliberately the same pair as Ledger.Adjust — see Permission.AuditRead.
            Permission.AuditRead,
            Permission.CategoryMappingsRead, Permission.CategoryMappingsManage,
            Permission.ImportsExecute, Permission.ReportsViewAll, Permission.ReconciliationClose,
            Permission.SubscriptionManage,
            Permission.SettingsUpdate, Permission.IntegrationsManage,
            // KAN-32. Manage is admin-only: deciding who has a login is not running compensation.
            Permission.UsersRead, Permission.UsersManage,
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
            Permission.PayoutsDeleteDraft, Permission.PayoutsDiscard,
            Permission.LedgerRead, Permission.LedgerAdjust, Permission.LedgerSummaryRead,
            Permission.LedgerCloseAccount,
            // KAN-19. Deliberately the same pair as Ledger.Adjust — see Permission.AuditRead.
            Permission.AuditRead,
            Permission.CategoryMappingsRead, Permission.CategoryMappingsManage,
            Permission.ImportsExecute, Permission.ReportsViewAll, Permission.ReconciliationClose,
            // KAN-32. Read WITHOUT Manage: knowing who approved a pay run is part of the job;
            // handing out logins is not.
            Permission.UsersRead,
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
            // KAN-93. PAYEES.READ IS DELIBERATELY ABSENT, AND ITS ABSENCE IS THE WHOLE FIX. A Rep held
            // it so they could "see their own record", and the cost of that sentence was a whole
            // section of the product they have no business in: the Payees entry in the rail, the
            // list, the empty state inviting them to add their first colleague, and the creation form
            // behind it. KAN-92 filtered the list by PayeeAccessGuard so the rows were at least their
            // own — but a screen showing one row, whose only offered action the server refuses, is not
            // a feature, it is a dead end with a button on it.
            //
            // ★ ONE LINE CLOSES THREE DOORS, and that is why the fix is here and not in the template.
            // The rail entry, the route guard (app.routes.ts) and ListPayeesHandler all ask this same
            // key, so removing it hides the menu item, refuses /payees typed by hand, and refuses the
            // endpoint — with no chance of the three disagreeing later. Hiding it in the sidebar alone
            // would have left the URL open; guarding the route alone would have left the menu lying.
            //
            // ★ THE REP LOSES NOTHING THEY USED. Their own figures come from /api/me/dashboard, which
            // enforces LedgerSummary.Read and resolves the payee from the TOKEN
            // (GetMyDashboardHandler) — it never asks this permission. Payees.Read is not how a person
            // reaches their own pay in this product, and it never was.
            //
            // ★ MANAGER KEEPS IT ON PURPOSE. A manager's job is explaining a reduced payment to their
            // reps, and PayeeAccessGuard already narrows them to their direct reports. This ticket is
            // about the Rep; widening it to Manager would be a different decision with a different
            // owner.
            Permission.AssignmentsRead,
            Permission.QuotasRead,
            // KAN-93 bug 6. THE RULES OF THE PLANS THEY ARE ON, READ-ONLY — and NOT Plans.Read, which
            // would hand them the tenant's whole catalogue, the version history and the simulator.
            // Clicking their own plan from the Assignments screen used to end in Access Denied, which
            // made "see why you were paid that" a promise the product could not keep.
            Permission.PlansReadOwn,
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
