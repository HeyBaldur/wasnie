/**
 * What a permission key MEANS, for the administrator deciding who should hold it.
 *
 * ★★ THIS FILE IS PRESENTATION, NOT AUTHORITY. Who holds what comes from the server
 * (`GET /api/users/roles`, straight out of `RolePermissions.cs`); this decides only how to SAY it and
 * in what order. The distinction matters: if these two were one file, adding a permission in C# would
 * silently change what the screen claims, and the screen would be right by accident. Here a key that
 * moves roles changes the answer immediately, and a key nobody has described yet is simply not shown.
 *
 * ★★ AN EXPLICIT LIST, NEVER A CONCATENATED KEY (§C2). `'ACCESS.' + permission` would print
 * `ACCESS.Payouts.SomethingNew` — an internal identifier — the day somebody adds a permission. The
 * unknown ones fall out of the list instead, which is silence rather than nonsense.
 *
 * ★ IT IS DELIBERATELY NOT EXHAUSTIVE. Thirty-one lines an administrator can act on beat fifty-four
 * that include `Transactions.UpdateFromExcel`; the answer to "what can this person do" is a decision,
 * not a dump. Every line here is one somebody would change a role over.
 */

export interface AccessCapability {
  /** Translation key for the sentence. Whitelisted — see the note above. */
  readonly labelKey: string;
  /** The permission that grants it, exactly as the server spells it. */
  readonly permission: string;
}

export interface AccessArea {
  readonly key: string;
  readonly titleKey: string;
  /** An `app-icon` name. */
  readonly icon: string;
  readonly capabilities: readonly AccessCapability[];
}

/**
 * ★ ORDERED BY WHAT IT WOULD COST TO GET WRONG, not alphabetically. "Their own pay" first because
 * everybody has it and it is the reassuring one; money and administration last because those are the
 * two an administrator scrolls to when they are about to change somebody's role.
 */
export const ACCESS_AREAS: readonly AccessArea[] = [
  {
    key: 'self',
    titleKey: 'ACCESS.AREA.SELF',
    icon: 'user',
    capabilities: [
      { labelKey: 'ACCESS.CAP.OWN_PAY', permission: 'LedgerSummary.Read' },
    ],
  },
  {
    key: 'people',
    titleKey: 'ACCESS.AREA.PEOPLE',
    icon: 'users',
    capabilities: [
      { labelKey: 'ACCESS.CAP.PAYEES_READ', permission: 'Payees.Read' },
      { labelKey: 'ACCESS.CAP.PAYEES_CREATE', permission: 'Payees.Create' },
      { labelKey: 'ACCESS.CAP.PAYEES_UPDATE', permission: 'Payees.Update' },
      { labelKey: 'ACCESS.CAP.PAYEES_TERMINATE', permission: 'Payees.Terminate' },
    ],
  },
  {
    key: 'plans',
    titleKey: 'ACCESS.AREA.PLANS',
    icon: 'file-text',
    capabilities: [
      { labelKey: 'ACCESS.CAP.PLANS_READ', permission: 'Plans.Read' },
      // ★ BOTH PLAN KEYS ARE LISTED, and that is the point of listing them. They are not a ladder:
      // an administrator holds Plans.Read and NOT Plans.ReadOwn, a rep the reverse. Showing only one
      // would make a rep's plan access look like nothing at all.
      { labelKey: 'ACCESS.CAP.PLANS_READ_OWN', permission: 'Plans.ReadOwn' },
      { labelKey: 'ACCESS.CAP.PLANS_CREATE', permission: 'Plans.Create' },
      { labelKey: 'ACCESS.CAP.PLANS_ACTIVATE', permission: 'Plans.Activate' },
      { labelKey: 'ACCESS.CAP.PLANS_STOP_RULE', permission: 'Plans.StopRule' },
    ],
  },
  {
    key: 'targets',
    titleKey: 'ACCESS.AREA.TARGETS',
    icon: 'target',
    capabilities: [
      { labelKey: 'ACCESS.CAP.QUOTAS_READ', permission: 'Quotas.Read' },
      { labelKey: 'ACCESS.CAP.QUOTAS_SET', permission: 'Quotas.Set' },
    ],
  },
  {
    key: 'assignments',
    titleKey: 'ACCESS.AREA.ASSIGNMENTS',
    icon: 'link-2',
    capabilities: [
      { labelKey: 'ACCESS.CAP.ASSIGNMENTS_READ', permission: 'Assignments.Read' },
      { labelKey: 'ACCESS.CAP.ASSIGNMENTS_CREATE', permission: 'Assignments.Create' },
      { labelKey: 'ACCESS.CAP.ASSIGNMENTS_DELETE', permission: 'Assignments.Delete' },
    ],
  },
  {
    key: 'sales',
    titleKey: 'ACCESS.AREA.SALES',
    icon: 'trend-up',
    capabilities: [
      { labelKey: 'ACCESS.CAP.TRANSACTIONS_READ', permission: 'Transactions.Read' },
      { labelKey: 'ACCESS.CAP.TRANSACTIONS_CREATE', permission: 'Transactions.Create' },
      { labelKey: 'ACCESS.CAP.TRANSACTIONS_VOID', permission: 'Transactions.Void' },
      { labelKey: 'ACCESS.CAP.CREDITS_READ', permission: 'Credits.Read' },
    ],
  },
  {
    key: 'pay',
    titleKey: 'ACCESS.AREA.PAY',
    icon: 'dollar-sign',
    capabilities: [
      { labelKey: 'ACCESS.CAP.PAYOUTS_READ', permission: 'Payouts.Read' },
      { labelKey: 'ACCESS.CAP.PAYOUTS_CALCULATE', permission: 'Payouts.Calculate' },
      { labelKey: 'ACCESS.CAP.PAYOUTS_APPROVE', permission: 'Payouts.Approve' },
      { labelKey: 'ACCESS.CAP.PAYOUTS_MARK_PAID', permission: 'Payouts.MarkPaid' },
      { labelKey: 'ACCESS.CAP.LEDGER_READ', permission: 'Ledger.Read' },
      { labelKey: 'ACCESS.CAP.LEDGER_ADJUST', permission: 'Ledger.Adjust' },
      { labelKey: 'ACCESS.CAP.RECONCILIATION_CLOSE', permission: 'Reconciliation.Close' },
    ],
  },
  {
    key: 'admin',
    titleKey: 'ACCESS.AREA.ADMIN',
    icon: 'settings',
    capabilities: [
      { labelKey: 'ACCESS.CAP.REPORTS_VIEW_ALL', permission: 'Reports.ViewAll' },
      { labelKey: 'ACCESS.CAP.USERS_MANAGE', permission: 'Users.Manage' },
      { labelKey: 'ACCESS.CAP.SETTINGS_UPDATE', permission: 'Settings.Update' },
      { labelKey: 'ACCESS.CAP.SUBSCRIPTION_MANAGE', permission: 'Subscription.Manage' },
      { labelKey: 'ACCESS.CAP.AUDIT_READ', permission: 'Audit.Read' },
    ],
  },
];

/** Every capability this screen can describe — the denominator of the coverage ring. */
export const TOTAL_CAPABILITIES = ACCESS_AREAS.reduce(
  (n, area) => n + area.capabilities.length,
  0,
);
