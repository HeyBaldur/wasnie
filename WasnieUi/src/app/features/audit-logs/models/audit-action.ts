/**
 * The translation whitelist for audit action codes.
 *
 * ★★ THE KEY IS NEVER BUILT BY CONCATENATION (§C2). `'AUDIT.ACTION.' + code` would look identical
 * and would print a raw internal identifier the first day somebody added an action this build had
 * never heard of. An explicit map cannot do that: an unknown code falls to the generic phrase, which
 * says the truthful thing instead of leaking a symbol.
 *
 * ★★ THE LOG HOLDS TWO CODES FOR ONE ACTION, AND BOTH ARE MAPPED HERE ON PURPOSE.
 * `VoidTransactionCommand` and `BulkVoidTransactionsHandler` write the literal `transaction_voided`
 * — no constant exists for it — and `RevertCommissionForLostDealCommand` writes
 * `deal_lost_commission_reverted` while the constant spells it in upper case. Measured in the
 * reference tenant: 18 rows of the first, and 7 lower-case against 3 upper-case of the second. Until
 * the writers are fixed, both spellings must read as the same sentence to a human, so the two
 * lower-case forms point at the SAME key as their upper-case twin. This is a display plaster over a
 * real defect in the trail, and the defect is raised as its own ticket — do not delete these two
 * lines until the log stops producing them AND the historical rows are dealt with.
 *
 * ★ ADDING A CODE IS A DELIBERATE ACT. A new action appears in the filter at once (the server serves
 * the list from the log) but renders as the generic label until somebody writes its three
 * translations. That is the correct order: the screen never claims to explain something nobody has
 * worded.
 */
const ACTION_KEYS: Readonly<Record<string, string>> = {

  ACCOUNT_LOCKED: 'AUDIT.ACTION.ACCOUNT_LOCKED',
  ASSIGNMENT_BULK_ACTIVATED: 'AUDIT.ACTION.ASSIGNMENT_BULK_ACTIVATED',
  ASSIGNMENT_BULK_DEACTIVATED: 'AUDIT.ACTION.ASSIGNMENT_BULK_DEACTIVATED',
  ASSIGNMENT_BULK_DELETED: 'AUDIT.ACTION.ASSIGNMENT_BULK_DELETED',
  ASSIGNMENT_CREATED: 'AUDIT.ACTION.ASSIGNMENT_CREATED',
  ASSIGNMENT_REMOVED: 'AUDIT.ACTION.ASSIGNMENT_REMOVED',
  ASSIGNMENT_UPDATED: 'AUDIT.ACTION.ASSIGNMENT_UPDATED',
  CREDITS_RECALCULATED: 'AUDIT.ACTION.CREDITS_RECALCULATED',
  CRM_AUTO_SYNC_COMPLETED: 'AUDIT.ACTION.CRM_AUTO_SYNC_COMPLETED',
  CRM_DEALS_IMPORTED: 'AUDIT.ACTION.CRM_DEALS_IMPORTED',
  CRM_DEAL_RECOVERED: 'AUDIT.ACTION.CRM_DEAL_RECOVERED',
  CRM_DRIFT_AUTO_RESOLVED: 'AUDIT.ACTION.CRM_DRIFT_AUTO_RESOLVED',
  CRM_DRIFT_DETECTED: 'AUDIT.ACTION.CRM_DRIFT_DETECTED',
  CRM_OWNER_LINKED: 'AUDIT.ACTION.CRM_OWNER_LINKED',
  DEAL_CHURN_CLAWBACK_POSTED: 'AUDIT.ACTION.DEAL_CHURN_CLAWBACK_POSTED',
  DEAL_LOST_COMMISSION_REVERTED: 'AUDIT.ACTION.DEAL_LOST_COMMISSION_REVERTED',
  DEAL_LOST_DETECTED: 'AUDIT.ACTION.DEAL_LOST_DETECTED',
  EMAIL_CONFIRMATION_SENT: 'AUDIT.ACTION.EMAIL_CONFIRMATION_SENT',
  EMAIL_CONFIRMED: 'AUDIT.ACTION.EMAIL_CONFIRMED',
  FIELD_REQUIREMENT_CHANGED: 'AUDIT.ACTION.FIELD_REQUIREMENT_CHANGED',
  HUBSPOT_CATEGORY_PROPERTY_CHANGED: 'AUDIT.ACTION.HUBSPOT_CATEGORY_PROPERTY_CHANGED',
  HUBSPOT_CONNECTED: 'AUDIT.ACTION.HUBSPOT_CONNECTED',
  HUBSPOT_DISCONNECTED: 'AUDIT.ACTION.HUBSPOT_DISCONNECTED',
  HUBSPOT_NEEDS_RECONNECT: 'AUDIT.ACTION.HUBSPOT_NEEDS_RECONNECT',
  HUBSPOT_RECONNECTED: 'AUDIT.ACTION.HUBSPOT_RECONNECTED',
  HUBSPOT_TOKEN_REFRESHED: 'AUDIT.ACTION.HUBSPOT_TOKEN_REFRESHED',
  LEDGER_ADJUSTMENT_CREATED: 'AUDIT.ACTION.LEDGER_ADJUSTMENT_CREATED',
  LOGIN_FAILURE: 'AUDIT.ACTION.LOGIN_FAILURE',
  LOGIN_SUCCESS: 'AUDIT.ACTION.LOGIN_SUCCESS',
  LOGOUT: 'AUDIT.ACTION.LOGOUT',
  PASSWORD_CHANGED: 'AUDIT.ACTION.PASSWORD_CHANGED',
  PASSWORD_RESET_COMPLETED: 'AUDIT.ACTION.PASSWORD_RESET_COMPLETED',
  PASSWORD_RESET_REQUESTED: 'AUDIT.ACTION.PASSWORD_RESET_REQUESTED',
  PAYEE_ACTIVATED: 'AUDIT.ACTION.PAYEE_ACTIVATED',
  PAYEE_CREATED: 'AUDIT.ACTION.PAYEE_CREATED',
  PAYEE_DEACTIVATED: 'AUDIT.ACTION.PAYEE_DEACTIVATED',
  PAYEE_DELETED: 'AUDIT.ACTION.PAYEE_DELETED',
  PAYEE_REACTIVATED: 'AUDIT.ACTION.PAYEE_REACTIVATED',
  PAYEE_TERMINATED: 'AUDIT.ACTION.PAYEE_TERMINATED',
  PAYEE_UPDATED: 'AUDIT.ACTION.PAYEE_UPDATED',
  PAYMENT_BLOCKED_DOUBLE_PAYMENT: 'AUDIT.ACTION.PAYMENT_BLOCKED_DOUBLE_PAYMENT',
  PAYOUT_APPROVED_WITH_OVERLAP: 'AUDIT.ACTION.PAYOUT_APPROVED_WITH_OVERLAP',
  PAYOUT_BULK_APPROVED_WITH_OVERLAP: 'AUDIT.ACTION.PAYOUT_BULK_APPROVED_WITH_OVERLAP',
  PAYOUT_BULK_PAID_WITH_OVERLAP: 'AUDIT.ACTION.PAYOUT_BULK_PAID_WITH_OVERLAP',
  PAYOUT_CREDITS_CONSUMED: 'AUDIT.ACTION.PAYOUT_CREDITS_CONSUMED',
  PAYOUT_DISCARDED: 'AUDIT.ACTION.PAYOUT_DISCARDED',
  PAYOUT_PAID_WITH_OVERLAP: 'AUDIT.ACTION.PAYOUT_PAID_WITH_OVERLAP',
  PAYOUT_REVERTED_TO_APPROVED: 'AUDIT.ACTION.PAYOUT_REVERTED_TO_APPROVED',
  PAY_RUN_APPROVED_WITH_OVERLAP: 'AUDIT.ACTION.PAY_RUN_APPROVED_WITH_OVERLAP',
  PAY_RUN_DRAFT_DELETED: 'AUDIT.ACTION.PAY_RUN_DRAFT_DELETED',
  PAY_RUN_PAID_WITH_OVERLAP: 'AUDIT.ACTION.PAY_RUN_PAID_WITH_OVERLAP',
  PENDING_TRANSACTIONS_PROCESSED: 'AUDIT.ACTION.PENDING_TRANSACTIONS_PROCESSED',
  PERMISSION_DENIED: 'AUDIT.ACTION.PERMISSION_DENIED',
  PLAN_ACTIVATED: 'AUDIT.ACTION.PLAN_ACTIVATED',
  PLAN_ARCHIVED: 'AUDIT.ACTION.PLAN_ARCHIVED',
  PLAN_CLAWBACK_POLICY_CHANGED: 'AUDIT.ACTION.PLAN_CLAWBACK_POLICY_CHANGED',
  PLAN_CREATED: 'AUDIT.ACTION.PLAN_CREATED',
  PLAN_RULE_ADDED: 'AUDIT.ACTION.PLAN_RULE_ADDED',
  PLAN_RULE_REMOVED: 'AUDIT.ACTION.PLAN_RULE_REMOVED',
  PLAN_RULE_STOPPED: 'AUDIT.ACTION.PLAN_RULE_STOPPED',
  PLAN_RULE_UPDATED: 'AUDIT.ACTION.PLAN_RULE_UPDATED',
  PLAN_SELECTED: 'AUDIT.ACTION.PLAN_SELECTED',
  PLAN_VERSION_CREATED: 'AUDIT.ACTION.PLAN_VERSION_CREATED',
  PROFILE_EMAIL_CHANGE_CONFIRMED: 'AUDIT.ACTION.PROFILE_EMAIL_CHANGE_CONFIRMED',
  PROFILE_EMAIL_CHANGE_REQUESTED: 'AUDIT.ACTION.PROFILE_EMAIL_CHANGE_REQUESTED',
  PROFILE_NAME_UPDATED: 'AUDIT.ACTION.PROFILE_NAME_UPDATED',
  PROFILE_PASSWORD_CHANGED: 'AUDIT.ACTION.PROFILE_PASSWORD_CHANGED',
  QUOTA_CREATED: 'AUDIT.ACTION.QUOTA_CREATED',
  QUOTA_DELETED: 'AUDIT.ACTION.QUOTA_DELETED',
  QUOTA_UPDATED: 'AUDIT.ACTION.QUOTA_UPDATED',
  RECONCILIATION_ROW_CLOSED: 'AUDIT.ACTION.RECONCILIATION_ROW_CLOSED',
  RECOVERY_CODES_REGENERATED: 'AUDIT.ACTION.RECOVERY_CODES_REGENERATED',
  RECOVERY_CODE_USED: 'AUDIT.ACTION.RECOVERY_CODE_USED',
  SUBSCRIPTION_ACTIVATED: 'AUDIT.ACTION.SUBSCRIPTION_ACTIVATED',
  SUBSCRIPTION_CANCELED: 'AUDIT.ACTION.SUBSCRIPTION_CANCELED',
  SUBSCRIPTION_CANCEL_REVERTED: 'AUDIT.ACTION.SUBSCRIPTION_CANCEL_REVERTED',
  SUBSCRIPTION_CANCEL_SCHEDULED: 'AUDIT.ACTION.SUBSCRIPTION_CANCEL_SCHEDULED',
  SUBSCRIPTION_DOWNGRADED: 'AUDIT.ACTION.SUBSCRIPTION_DOWNGRADED',
  SUBSCRIPTION_PAST_DUE: 'AUDIT.ACTION.SUBSCRIPTION_PAST_DUE',
  SUBSCRIPTION_RECOVERED: 'AUDIT.ACTION.SUBSCRIPTION_RECOVERED',
  SUBSCRIPTION_TIER_SYNCED_FROM_STRIPE: 'AUDIT.ACTION.SUBSCRIPTION_TIER_SYNCED_FROM_STRIPE',
  SUBSCRIPTION_UPGRADED: 'AUDIT.ACTION.SUBSCRIPTION_UPGRADED',
  TENANT_QUALIFIED: 'AUDIT.ACTION.TENANT_QUALIFIED',
  TENANT_REGISTERED: 'AUDIT.ACTION.TENANT_REGISTERED',
  TERMINATED_ACCOUNT_CLOSED: 'AUDIT.ACTION.TERMINATED_ACCOUNT_CLOSED',
  TIER_LIMIT_EXCEEDED: 'AUDIT.ACTION.TIER_LIMIT_EXCEEDED',
  TOKEN_REFRESHED: 'AUDIT.ACTION.TOKEN_REFRESHED',
  TRANSACTION_INGESTED: 'AUDIT.ACTION.TRANSACTION_INGESTED',
  TRANSACTION_MARKED_PAID: 'AUDIT.ACTION.TRANSACTION_MARKED_PAID',
  TRANSACTION_PAYEE_ASSIGNED: 'AUDIT.ACTION.TRANSACTION_PAYEE_ASSIGNED',
  TRANSACTION_PAYEE_REASSIGNED: 'AUDIT.ACTION.TRANSACTION_PAYEE_REASSIGNED',
  TRANSACTION_UPDATED_VIA_EXCEL: 'AUDIT.ACTION.TRANSACTION_UPDATED_VIA_EXCEL',
  TRANSACTION_VOIDED: 'AUDIT.ACTION.TRANSACTION_VOIDED',
  TWO_FACTOR_DISABLED: 'AUDIT.ACTION.TWO_FACTOR_DISABLED',
  TWO_FACTOR_ENABLED: 'AUDIT.ACTION.TWO_FACTOR_ENABLED',
  TWO_FACTOR_LOGIN_FAILURE: 'AUDIT.ACTION.TWO_FACTOR_LOGIN_FAILURE',
  TWO_FACTOR_LOGIN_SUCCESS: 'AUDIT.ACTION.TWO_FACTOR_LOGIN_SUCCESS',

  // ★ The two lower-case spellings the log actually contains — see the note above. Same keys as
  // their upper-case twins, so the table reads identically whichever variant a row carries.
  transaction_voided: 'AUDIT.ACTION.TRANSACTION_VOIDED',
  deal_lost_commission_reverted: 'AUDIT.ACTION.DEAL_LOST_COMMISSION_REVERTED',
};

export const UNKNOWN_ACTION_KEY = 'AUDIT.ACTION.UNKNOWN';

/**
 * ★★ OWN PROPERTIES ONLY — `hasOwnProperty`, NOT `in` AND NOT `?? fallback`. An object literal
 * inherits from `Object.prototype`, so `ACTION_KEYS['toString']` is a FUNCTION rather than
 * `undefined`: `??` would not fire, and the table would render `function toString() { [native
 * code] }` where an action should be. `'constructor' in ACTION_KEYS` is `true` for the same reason.
 * The `Action` column holds whatever a writer put there, so this is not hypothetical enough to
 * ignore — and the fix is one call.
 */
function own(map: Readonly<Record<string, string>>, key: string): string | null {
  return Object.prototype.hasOwnProperty.call(map, key) ? map[key] : null;
}

/** The translation key for an action code, or the generic one. Never the code itself. */
export function actionKey(code: string | null | undefined): string {
  if (!code) return UNKNOWN_ACTION_KEY;
  return own(ACTION_KEYS, code) ?? UNKNOWN_ACTION_KEY;
}

export function isKnownAction(code: string | null | undefined): boolean {
  return !!code && own(ACTION_KEYS, code) !== null;
}

/**
 * Where a resource type's entity lives in the app.
 *
 * ★★ ABSENCE IS THE POINT, NOT AN OMISSION. `Auth`, `Subscription`, `Integration`,
 * `FieldRequirement`, `Reconciliation` and `Credit` have no per-entity screen to land on, so their
 * rows render the resource as plain text. This is the same rule KAN-49 settled for the
 * Reconciliation Centre's Resolve column: a row with no destination shows nothing rather than a link
 * that goes nowhere. A sign-in is not an entity anybody can open, and pretending otherwise costs the
 * reader a click and their place in the table.
 *
 * ★ THE ID IS NOT VALIDATED HERE. The log stores whatever the writer put in `ResourceId`; a route
 * built from a malformed one lands on a not-found screen, which is the honest outcome and strictly
 * better than a validation on the READ path that would blank out historical rows (§D1).
 */
const RESOURCE_ROUTES: Readonly<Record<string, string>> = {
  Payee: '/payees',
  Plan: '/plans',
  Quota: '/quotas',
  Assignment: '/assignments',
  Transaction: '/transactions',
  Payout: '/payouts',
};

/** The router commands for a row's entity, or null when its type has no screen. */
export function resourceLink(
  resourceType: string | null | undefined,
  resourceId: string | null | undefined,
): readonly string[] | null {
  if (!resourceType || !resourceId) return null;
  // ★ Own properties only, for the same reason as `actionKey` — see `own` above. `ResourceType` is
  // a free-text column, and a row carrying "constructor" would otherwise build a route out of a
  // function.
  const base = own(RESOURCE_ROUTES, resourceType);
  return base ? [base, resourceId] : null;
}
