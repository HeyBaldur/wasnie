namespace Wasnie.Domain.Audit;

public static class AuditActions
{
    // Authentication
    public const string LoginSuccess = "LOGIN_SUCCESS";
    public const string LoginFailure = "LOGIN_FAILURE";
    public const string Logout = "LOGOUT";
    public const string PasswordChanged = "PASSWORD_CHANGED";
    public const string PasswordResetRequested = "PASSWORD_RESET_REQUESTED";
    public const string PasswordResetCompleted = "PASSWORD_RESET_COMPLETED";
    public const string TokenRefreshed = "TOKEN_REFRESHED";
    public const string AccountLocked = "ACCOUNT_LOCKED";
    public const string TenantRegistered = "TENANT_REGISTERED";
    public const string EmailConfirmationSent = "EMAIL_CONFIRMATION_SENT";
    public const string EmailConfirmed = "EMAIL_CONFIRMED";
    public const string TenantQualified = "TENANT_QUALIFIED";

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
    public const string AssignmentBulkActivated = "ASSIGNMENT_BULK_ACTIVATED";
    public const string AssignmentBulkDeactivated = "ASSIGNMENT_BULK_DEACTIVATED";
    public const string AssignmentBulkDeleted = "ASSIGNMENT_BULK_DELETED";

    // Transactions (Phase 2)
    public const string TransactionIngested = "TRANSACTION_INGESTED";
    public const string TransactionPayeeAssigned = "TRANSACTION_PAYEE_ASSIGNED";
    public const string TransactionPayeeReassigned = "TRANSACTION_PAYEE_REASSIGNED";
    public const string PendingTransactionsProcessed = "PENDING_TRANSACTIONS_PROCESSED";
    public const string TransactionUpdatedViaExcel = "TRANSACTION_UPDATED_VIA_EXCEL";

    // Transactions (Phase 3 — payout propagation)
    public const string TransactionMarkedPaid = "TRANSACTION_MARKED_PAID";

    // Payouts — credit consumption (anti-double-pay Phase 3)
    public const string PayoutCreditsConsumed = "PAYOUT_CREDITS_CONSUMED";
    public const string PayoutRevertedToApproved = "PAYOUT_REVERTED_TO_APPROVED";
    public const string PaymentBlockedDoublePayment = "PAYMENT_BLOCKED_DOUBLE_PAYMENT";

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

    // Profile self-service
    public const string ProfileNameUpdated = "PROFILE_NAME_UPDATED";
    public const string ProfilePasswordChanged = "PROFILE_PASSWORD_CHANGED";
    public const string ProfileEmailChangeRequested = "PROFILE_EMAIL_CHANGE_REQUESTED";
    public const string ProfileEmailChangeConfirmed = "PROFILE_EMAIL_CHANGE_CONFIRMED";

    // Two-factor authentication
    public const string TwoFactorEnabled = "TWO_FACTOR_ENABLED";
    public const string TwoFactorDisabled = "TWO_FACTOR_DISABLED";
    public const string TwoFactorLoginSuccess = "TWO_FACTOR_LOGIN_SUCCESS";
    public const string TwoFactorLoginFailure = "TWO_FACTOR_LOGIN_FAILURE";
    public const string RecoveryCodeUsed = "RECOVERY_CODE_USED";
    public const string RecoveryCodesRegenerated = "RECOVERY_CODES_REGENERATED";

    // Pay runs — lifecycle
    public const string PayRunDraftDeleted = "PAY_RUN_DRAFT_DELETED";

    // Credits
    public const string CreditsRecalculated = "CREDITS_RECALCULATED";

    // Pay runs — overlap override audit (anti-double-pay)
    public const string PayRunApprovedWithOverlap = "PAY_RUN_APPROVED_WITH_OVERLAP";
    public const string PayRunPaidWithOverlap = "PAY_RUN_PAID_WITH_OVERLAP";

    // Payouts — overlap override audit (anti-double-pay)
    public const string PayoutApprovedWithOverlap = "PAYOUT_APPROVED_WITH_OVERLAP";
    public const string PayoutPaidWithOverlap = "PAYOUT_PAID_WITH_OVERLAP";
    public const string PayoutBulkApprovedWithOverlap = "PAYOUT_BULK_APPROVED_WITH_OVERLAP";
    public const string PayoutBulkPaidWithOverlap = "PAYOUT_BULK_PAID_WITH_OVERLAP";

    // Authorization denials (Rule 5.1.4)
    public const string PermissionDenied = "PERMISSION_DENIED";
    public const string TierLimitExceeded = "TIER_LIMIT_EXCEEDED";

    // Integrations — HubSpot OAuth (Phase 1). Token values are NEVER included in audit metadata.
    public const string HubSpotConnected = "HUBSPOT_CONNECTED";
    public const string HubSpotReconnected = "HUBSPOT_RECONNECTED";
    public const string HubSpotDisconnected = "HUBSPOT_DISCONNECTED";
    public const string HubSpotNeedsReconnect = "HUBSPOT_NEEDS_RECONNECT";
    public const string HubSpotTokenRefreshed = "HUBSPOT_TOKEN_REFRESHED";

    // Integrations — CRM deal ingestion (Phase 2). Money-relevant: owner→payee links decide who is paid.
    public const string CrmDealsImported = "CRM_DEALS_IMPORTED";
    public const string CrmOwnerLinked = "CRM_OWNER_LINKED";

    // Integrations — CRM automatic polling sync (Phase 3). One entry per tenant per successful run.
    public const string CrmAutoSyncCompleted = "CRM_AUTO_SYNC_COMPLETED";

    // Integrations — CRM drift policy. A deal's amount/close-date changed after import.
    // Auto-resolved: the still-Pending transaction was voided and re-created with the new values.
    public const string CrmDriftAutoResolved = "CRM_DRIFT_AUTO_RESOLVED";
    // Detected: the transaction was already Calculated/Paid (immutable) — recorded as an alert, untouched.
    public const string CrmDriftDetected = "CRM_DRIFT_DETECTED";
}
