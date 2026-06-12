namespace Wasnie.Domain.Audit;

public static class AuditActions
{
    // Authentication
    public const string LoginSuccess = "LOGIN_SUCCESS";
    public const string LoginFailure = "LOGIN_FAILURE";
    public const string Logout = "LOGOUT";
    public const string PasswordChanged = "PASSWORD_CHANGED";
    public const string TokenRefreshed = "TOKEN_REFRESHED";
    public const string AccountLocked = "ACCOUNT_LOCKED";

    // Payees
    public const string PayeeCreated = "PAYEE_CREATED";
    public const string PayeeUpdated = "PAYEE_UPDATED";
    public const string PayeeTerminated = "PAYEE_TERMINATED";
    public const string PayeeReactivated = "PAYEE_REACTIVATED";
    public const string PayeeDeleted = "PAYEE_DELETED";
    public const string PayeeDeactivated = "PAYEE_DEACTIVATED";
    public const string PayeeActivated = "PAYEE_ACTIVATED";

    // Plans
    public const string PlanCreated = "PLAN_CREATED";
    public const string PlanActivated = "PLAN_ACTIVATED";
    public const string PlanArchived = "PLAN_ARCHIVED";
    public const string PlanVersionCreated = "PLAN_VERSION_CREATED";
    public const string PlanRuleAdded = "PLAN_RULE_ADDED";
    public const string PlanRuleUpdated = "PLAN_RULE_UPDATED";
    public const string PlanRuleRemoved = "PLAN_RULE_REMOVED";

    // Quotas
    public const string QuotaCreated = "QUOTA_CREATED";
    public const string QuotaUpdated = "QUOTA_UPDATED";
    public const string QuotaDeleted = "QUOTA_DELETED";

    // Assignments
    public const string AssignmentCreated = "ASSIGNMENT_CREATED";
    public const string AssignmentUpdated = "ASSIGNMENT_UPDATED";
    public const string AssignmentRemoved = "ASSIGNMENT_REMOVED";

    // Transactions (Phase 2)
    public const string TransactionIngested = "TRANSACTION_INGESTED";
    public const string TransactionPayeeAssigned = "TRANSACTION_PAYEE_ASSIGNED";
    public const string TransactionPayeeReassigned = "TRANSACTION_PAYEE_REASSIGNED";
    public const string PendingTransactionsProcessed = "PENDING_TRANSACTIONS_PROCESSED";
    public const string TransactionUpdatedViaExcel = "TRANSACTION_UPDATED_VIA_EXCEL";

    // Settings (Rule 5.1.5 — configuration changes)
    public const string FieldRequirementChanged = "FIELD_REQUIREMENT_CHANGED";

    // Subscription
    public const string PlanSelected = "PLAN_SELECTED";
    public const string SubscriptionActivated = "SUBSCRIPTION_ACTIVATED";
    public const string SubscriptionUpgraded = "SUBSCRIPTION_UPGRADED";
    public const string SubscriptionDowngraded = "SUBSCRIPTION_DOWNGRADED";
    public const string SubscriptionCanceled = "SUBSCRIPTION_CANCELED";
    public const string SubscriptionPastDue = "SUBSCRIPTION_PAST_DUE";
    public const string SubscriptionRecovered = "SUBSCRIPTION_RECOVERED";
    public const string SubscriptionCancelScheduled = "SUBSCRIPTION_CANCEL_SCHEDULED";
    public const string SubscriptionCancelReverted = "SUBSCRIPTION_CANCEL_REVERTED";
    public const string SubscriptionTierSyncedFromStripe = "SUBSCRIPTION_TIER_SYNCED_FROM_STRIPE";

    // Authorization denials (Rule 5.1.4)
    public const string PermissionDenied = "PERMISSION_DENIED";
    public const string TierLimitExceeded = "TIER_LIMIT_EXCEEDED";
}
