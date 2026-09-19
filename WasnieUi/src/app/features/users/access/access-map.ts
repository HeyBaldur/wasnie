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

  /**
   * The permissions that grant it — ANY of them is enough.
   *
   * ★★ IT IS A LIST BECAUSE SOME KEYS IMPLY OTHERS, and a one-to-one mapping got that wrong on screen.
   * An administrator holds `Plans.Read` and not `Plans.ReadOwn`, so "see the plans they are paid under"
   * rendered as a red cross on the account that can read every plan in the workspace. The server was
   * right all along — `PlanAccessGuard` returns true for `Plans.Read` before it ever looks at
   * `ReadOwn` — and only this screen was wrong, which is the worst place for it to be wrong.
   */
  readonly permissions: readonly string[];
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
      { labelKey: 'ACCESS.CAP.OWN_PAY', permissions: ['LedgerSummary.Read'] },
    ],
  },
  {
    key: 'people',
    titleKey: 'ACCESS.AREA.PEOPLE',
    icon: 'users',
    capabilities: [
      { labelKey: 'ACCESS.CAP.PAYEES_READ', permissions: ['Payees.Read'] },
      { labelKey: 'ACCESS.CAP.PAYEES_CREATE', permissions: ['Payees.Create'] },
      { labelKey: 'ACCESS.CAP.PAYEES_UPDATE', permissions: ['Payees.Update'] },
      { labelKey: 'ACCESS.CAP.PAYEES_TERMINATE', permissions: ['Payees.Terminate'] },
    ],
  },
  {
    key: 'plans',
    titleKey: 'ACCESS.AREA.PLANS',
    icon: 'file-text',
    capabilities: [
      { labelKey: 'ACCESS.CAP.PLANS_READ', permissions: ['Plans.Read'] },
      // ★★ GRANTED BY EITHER KEY, AND THE FIRST VERSION OF THIS LINE WAS WRONG. It listed only
      // `Plans.ReadOwn` on the reasoning that the two are "not a ladder" — which is false for reading:
      // `PlanAccessGuard` returns true for `Plans.Read` before it ever tests `ReadOwn`, and
      // `GetTriggerFieldsHandler` falls through to `Plans.Read` the same way. So an administrator CAN
      // see the plans they are paid under, and the panel was telling them they could not.
      { labelKey: 'ACCESS.CAP.PLANS_READ_OWN', permissions: ['Plans.Read', 'Plans.ReadOwn'] },
      { labelKey: 'ACCESS.CAP.PLANS_CREATE', permissions: ['Plans.Create'] },
      { labelKey: 'ACCESS.CAP.PLANS_ACTIVATE', permissions: ['Plans.Activate'] },
      { labelKey: 'ACCESS.CAP.PLANS_STOP_RULE', permissions: ['Plans.StopRule'] },
    ],
  },
  {
    key: 'targets',
    titleKey: 'ACCESS.AREA.TARGETS',
    icon: 'target',
    capabilities: [
      { labelKey: 'ACCESS.CAP.QUOTAS_READ', permissions: ['Quotas.Read'] },
      { labelKey: 'ACCESS.CAP.QUOTAS_SET', permissions: ['Quotas.Set'] },
    ],
  },
  {
    key: 'assignments',
    titleKey: 'ACCESS.AREA.ASSIGNMENTS',
    icon: 'link-2',
    capabilities: [
      { labelKey: 'ACCESS.CAP.ASSIGNMENTS_READ', permissions: ['Assignments.Read'] },
      { labelKey: 'ACCESS.CAP.ASSIGNMENTS_CREATE', permissions: ['Assignments.Create'] },
      { labelKey: 'ACCESS.CAP.ASSIGNMENTS_DELETE', permissions: ['Assignments.Delete'] },
    ],
  },
  {
    key: 'sales',
    titleKey: 'ACCESS.AREA.SALES',
    icon: 'trend-up',
    capabilities: [
      { labelKey: 'ACCESS.CAP.TRANSACTIONS_READ', permissions: ['Transactions.Read'] },
      { labelKey: 'ACCESS.CAP.TRANSACTIONS_CREATE', permissions: ['Transactions.Create'] },
      { labelKey: 'ACCESS.CAP.TRANSACTIONS_VOID', permissions: ['Transactions.Void'] },
      { labelKey: 'ACCESS.CAP.CREDITS_READ', permissions: ['Credits.Read'] },
    ],
  },
  {
    key: 'pay',
    titleKey: 'ACCESS.AREA.PAY',
    icon: 'dollar-sign',
    capabilities: [
      { labelKey: 'ACCESS.CAP.PAYOUTS_READ', permissions: ['Payouts.Read'] },
      { labelKey: 'ACCESS.CAP.PAYOUTS_CALCULATE', permissions: ['Payouts.Calculate'] },
      { labelKey: 'ACCESS.CAP.PAYOUTS_APPROVE', permissions: ['Payouts.Approve'] },
      { labelKey: 'ACCESS.CAP.PAYOUTS_MARK_PAID', permissions: ['Payouts.MarkPaid'] },
      { labelKey: 'ACCESS.CAP.LEDGER_READ', permissions: ['Ledger.Read'] },
      { labelKey: 'ACCESS.CAP.LEDGER_ADJUST', permissions: ['Ledger.Adjust'] },
      { labelKey: 'ACCESS.CAP.RECONCILIATION_CLOSE', permissions: ['Reconciliation.Close'] },
    ],
  },
  {
    key: 'admin',
    titleKey: 'ACCESS.AREA.ADMIN',
    icon: 'settings',
    capabilities: [
      { labelKey: 'ACCESS.CAP.REPORTS_VIEW_ALL', permissions: ['Reports.ViewAll'] },
      { labelKey: 'ACCESS.CAP.USERS_MANAGE', permissions: ['Users.Manage'] },
      { labelKey: 'ACCESS.CAP.SETTINGS_UPDATE', permissions: ['Settings.Update'] },
      { labelKey: 'ACCESS.CAP.SUBSCRIPTION_MANAGE', permissions: ['Subscription.Manage'] },
      { labelKey: 'ACCESS.CAP.AUDIT_READ', permissions: ['Audit.Read'] },
    ],
  },
];

/** Every capability this screen can describe — the denominator of the coverage ring. */
export const TOTAL_CAPABILITIES = ACCESS_AREAS.reduce(
  (n, area) => n + area.capabilities.length,
  0,
);
