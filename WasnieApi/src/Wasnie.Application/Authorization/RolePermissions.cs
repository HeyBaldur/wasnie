using Wasnie.Domain.Authorization;

namespace Wasnie.Application.Authorization;

public static class RolePermissions
{
    private static readonly IReadOnlySet<string> TenantAdminPermissions =
        new HashSet<string>(StringComparer.Ordinal)
        {
            Permission.PayeesRead, Permission.PayeesCreate, Permission.PayeesUpdate, Permission.PayeesTerminate,
            Permission.PlansRead, Permission.PlansCreate, Permission.PlansUpdate,
            Permission.PlansDelete, Permission.PlansActivate, Permission.PlansArchive,
            Permission.QuotasRead, Permission.QuotasSet, Permission.QuotasUpdate,
            Permission.AssignmentsRead, Permission.AssignmentsCreate, Permission.AssignmentsUpdate,
            Permission.TransactionsCreate, Permission.TransactionsRead,
            Permission.ImportsExecute, Permission.ReportsViewAll, Permission.SubscriptionManage,
        };

    private static readonly IReadOnlySet<string> CompManagerPermissions =
        new HashSet<string>(StringComparer.Ordinal)
        {
            Permission.PayeesRead, Permission.PayeesCreate, Permission.PayeesUpdate, Permission.PayeesTerminate,
            Permission.PlansRead, Permission.PlansCreate, Permission.PlansUpdate,
            Permission.PlansDelete, Permission.PlansActivate, Permission.PlansArchive,
            Permission.QuotasRead, Permission.QuotasSet, Permission.QuotasUpdate,
            Permission.AssignmentsRead, Permission.AssignmentsCreate, Permission.AssignmentsUpdate,
            Permission.TransactionsCreate, Permission.TransactionsRead,
            Permission.ImportsExecute, Permission.ReportsViewAll,
        };

    private static readonly IReadOnlySet<string> ManagerPermissions =
        new HashSet<string>(StringComparer.Ordinal)
        {
            Permission.PayeesRead,
            Permission.QuotasRead,
            Permission.AssignmentsRead,
        };

    private static readonly IReadOnlySet<string> RepPermissions =
        new HashSet<string>(StringComparer.Ordinal)
        {
            Permission.PayeesRead,
            Permission.AssignmentsRead,
            Permission.QuotasRead,
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
