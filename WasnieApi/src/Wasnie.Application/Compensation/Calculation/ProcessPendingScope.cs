namespace Wasnie.Application.Compensation.Calculation;

/// <summary>
/// Determines which pending transactions the ProcessPendingTransactionsJob targets.
/// </summary>
public enum ProcessPendingScope
{
    /// <summary>Process Pending for the payee covered by a specific PlanAssignment, within the assignment's effective period.</summary>
    ByPlanAssignment,

    /// <summary>Process Pending for all payees with active assignments to a plan, within the plan's effective period.</summary>
    ByPlan,

    /// <summary>Process Pending for one payee within an explicit date range.</summary>
    ByPayeeAndPeriod,
}
