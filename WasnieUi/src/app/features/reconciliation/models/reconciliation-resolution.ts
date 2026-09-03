import { ReconciliationRow } from './reconciliation.model';

/**
 * What "Resolve" does for a given row — the Paso 0 mapping of KAN-49, as code.
 *
 * ★★ THE WHOLE POINT IS THAT SOME REASONS HAVE NO CURE, AND THE SCREEN MUST ADMIT IT. The ticket
 * called this out as the classic blind spot: assume every reason has a fix screen, and the design
 * stalls halfway through the sprint on the one that does not. Mapping it here, before any markup,
 * means an unmapped reason produces a row with NO action rather than a button that navigates
 * nowhere. `NoQuotaInEffect` is exactly that case today — see below.
 *
 * ★★ NOTHING HERE MUTATES A ROW TO HIDE IT. A deep link changes no state at all; the reprocess
 * action runs the engine that already exists. In both cases the row leaves the queue because the
 * condition behind it stopped being true — the queue derives from the data (KAN-28), so there is no
 * "resolved" flag to set and none to go stale.
 *
 * ★ THE MAP IS EXPLICIT, NEVER COMPUTED FROM THE CODE (§C2, same rule as the translation
 * whitelist). A reason this build has never heard of gets no action, not a route assembled from a
 * string it cannot validate.
 */

/** Navigate somewhere that can fix the underlying data. Changes nothing by itself. */
export interface DeepLinkResolution {
  readonly kind: 'link';
  /** routerLink commands. */
  readonly commands: unknown[];
  readonly queryParams?: Record<string, string>;
  /** Translation key for the visible action text. */
  readonly labelKey: string;
  /**
   * Translation key for the tooltip, when the destination needs more than its own name to be
   * useful. Defaults to `labelKey`.
   *
   * ★★ THIS IS WHAT REPLACES A DATA CHANGE. The ladder refusals are about ONE rule and the row does
   * not carry a rule id, so the link can only reach the plan. Rather than widen the DTO, the tooltip
   * says what the link cannot: that a rule of THIS plan refused to price the sale, and that its
   * ladders are what to look at. The reader lands on the plan knowing what they came for.
   *
   * ★ IT IS A CODE, NOT A SENTENCE (§C1). The wording and the language belong to the screen.
   */
  readonly titleKey?: string;
}

/**
 * Run the existing manual reprocess, scoped to this row's payee and date.
 *
 * ★ IT DISPATCHES THE ENGINE THAT IS ALREADY THERE — `ProcessPendingScope.ByPayeeAndPeriod`, the
 * same job the plan and assignment screens dispatch. No new engine, and nothing is paid: credits
 * enter the existing pay run → approval → payment flow, every step of it manual.
 */
export interface ReprocessResolution {
  readonly kind: 'reprocess';
  readonly payeeId: string;
  /** Start and end are the row's own date: the narrowest scope that can reach this transaction. */
  readonly periodStart: string;
  readonly periodEnd: string;
  readonly labelKey: string;
}

export type ReconciliationResolution = DeepLinkResolution | ReprocessResolution;

/**
 * The reason a row offers no action, for the tooltip on the disabled cell.
 *
 * ★ TWO DIFFERENT SILENCES, AND THEY ARE NOT THE SAME MESSAGE (§B3). "This reason has no cure
 * screen yet" is a gap in the product; "this row is missing the id the link needs" is a gap in this
 * particular row. Telling a user to wait for a feature when the real problem is a null payee would
 * send them to wait for something that was never going to help.
 */
export type NoResolutionReason = 'NO_CURE_SCREEN' | 'ROW_LACKS_IDENTIFIERS';

/**
 * The reasons whose cure screen does not exist yet.
 *
 * ★★ `NoQuotaInEffect` WAS HERE AND WAS WRONG. Paso 0 looked for a "quotas of this payee" screen —
 * the destination the ticket named — established that none exists, and stopped there. It should not
 * have: the cure for "this payee has no quota" is to CREATE one, and `/quotas/new` has existed all
 * along. Looking for the screen the ticket described instead of the screen the cure needs is how a
 * reachable destination gets recorded as unreachable.
 *
 * ★ `CurrencyMismatch` is the one that genuinely stays. It waits on KAN-45, which owns the decision
 * of what fixing it even means — re-denominate the plan, or route the sale to another one. Those
 * lead to different screens and the choice is not this ticket's to make, so a link now would be a
 * guess. The Paso 0 rule holds for it, and for any future reason in the same position: no button,
 * a tooltip that says why, never a deep link that goes somewhere unhelpful.
 */
const WITHOUT_CURE_SCREEN: ReadonlySet<string> = new Set([
  'CurrencyMismatch',
]);

/**
 * The action for a row, or null when it has none.
 *
 * ★ ONE ROW, MANY REASONS, ONE BUTTON. The first mapped reason wins, in the order the row carries
 * them. A row that fails for two things needs its first fix before the second becomes reachable
 * anyway, and two buttons in one cell would ask the reader to choose an order the data already
 * implies.
 */
export function resolutionFor(row: ReconciliationRow): ReconciliationResolution | null {
  for (const reason of row.reasons) {
    const resolution = resolutionForReason(reason, row);
    if (resolution) return resolution;
  }
  return null;
}

/** Why a row has no action. Only meaningful when `resolutionFor` returned null. */
export function noResolutionReason(row: ReconciliationRow): NoResolutionReason {
  return row.reasons.some((r) => WITHOUT_CURE_SCREEN.has(r))
    ? 'NO_CURE_SCREEN'
    : 'ROW_LACKS_IDENTIFIERS';
}

/**
 * The translation key explaining why a row offers nothing.
 *
 * ★ SPELLED OUT, NOT ASSEMBLED (§C2). `'RECONCILIATION.RESOLVE.NONE.' + reason` would work until
 * the union gained a member nobody translated, and then it would print an internal identifier in a
 * tooltip on a finance screen. The map is exhaustive over the union, so adding a member without a
 * key is a compile error rather than a runtime leak.
 */
const NO_RESOLUTION_KEYS: Readonly<Record<NoResolutionReason, string>> = {
  NO_CURE_SCREEN: 'RECONCILIATION.RESOLVE.NONE_NO_CURE_SCREEN',
  ROW_LACKS_IDENTIFIERS: 'RECONCILIATION.RESOLVE.NONE_ROW_LACKS_IDENTIFIERS',
};

export function noResolutionKey(row: ReconciliationRow): string {
  return NO_RESOLUTION_KEYS[noResolutionReason(row)];
}

function resolutionForReason(
  reason: string,
  row: ReconciliationRow,
): ReconciliationResolution | null {
  switch (reason) {
    // ── The one action that runs something (KAN-50's reason) ──────────────────────────────
    //
    // ★ NEEDS BOTH THE PAYEE AND THE DATE, because that is what ByPayeeAndPeriod is validated to
    // require server-side. Without either, no button: an action that would 400 is not an action.
    case 'ProcessableWithoutCredit':
      return row.payeeId && row.periodDate
        ? {
            kind: 'reprocess',
            payeeId: row.payeeId,
            periodStart: row.periodDate,
            periodEnd: row.periodDate,
            labelKey: 'RECONCILIATION.RESOLVE.REPROCESS',
          }
        : null;

    // ── Data fixes: navigate, mutate nothing ──────────────────────────────────────────────
    //
    // ★ THE ASSIGNMENT FORM PRE-FILLS FROM ?payeeId=, verified in `assignments/create`. The link
    // lands on a form that already knows who it is for.
    case 'NoActiveAssignment':
      return row.payeeId
        ? {
            kind: 'link',
            commands: ['/assignments', 'new'],
            queryParams: { payeeId: row.payeeId },
            labelKey: 'RECONCILIATION.RESOLVE.CREATE_ASSIGNMENT',
          }
        : null;

    // ★ THE CURE IS THE ASSIGNMENTS, NOT THE TRANSACTION. Ambiguity means the payee holds two
    // eligible plans; the transaction is only where it showed up. The dashboard's equivalent link
    // goes to the payee, this one goes one step further to the list that can actually be edited.
    case 'AmbiguousAttribution':
      return row.payeeId
        ? {
            kind: 'link',
            commands: ['/assignments'],
            queryParams: { payeeId: row.payeeId },
            labelKey: 'RECONCILIATION.RESOLVE.REVIEW_ASSIGNMENTS',
          }
        : null;

    // ★ TO THE LIST, NOT THE DETAIL, and that is not a shortcut. The "assign payee" modal lives on
    // the transactions LIST; the detail screen is read-only. `?ref=` is the same parameter the
    // dashboard's drift link already uses, so the reader lands on one row with the action on it.
    case 'NoPayee':
      return row.referenceNumber
        ? {
            kind: 'link',
            commands: ['/transactions'],
            queryParams: { ref: row.referenceNumber },
            labelKey: 'RECONCILIATION.RESOLVE.ASSIGN_PAYEE',
          }
        : null;

    case 'DealLost':
    case 'CrmDrift':
      return row.referenceNumber
        ? {
            kind: 'link',
            commands: ['/transactions'],
            queryParams: { ref: row.referenceNumber },
            labelKey: 'RECONCILIATION.RESOLVE.REVIEW_TRANSACTION',
          }
        : null;

    // ── The payee has no quota: create one ────────────────────────────────────────────────
    //
    // ★★ `/quotas/new` PRE-FILLS FROM ?payeeId=, verified in `quotas/create` — it reads the param,
    // resolves the payee's label and adds them to the form. So the link does not merely land on the
    // right screen, it lands on a form that already knows who it is for.
    //
    // ★ WITHOUT A PAYEE IT STILL LINKS, bare. Creating a quota is the cure either way; the pre-fill
    // is a convenience, not the reason to navigate. This is the one place where a missing id
    // degrades the action instead of removing it.
    case 'NoQuotaInEffect':
      return {
        kind: 'link',
        commands: ['/quotas', 'new'],
        queryParams: row.payeeId ? { payeeId: row.payeeId } : undefined,
        labelKey: 'RECONCILIATION.RESOLVE.CREATE_QUOTA',
      };

    // ── Plan-level fixes ──────────────────────────────────────────────────────────────────
    //
    // ★★ THE PLAN, NOT THE RULE, AND THE TOOLTIP CARRIES WHAT THE LINK CANNOT. These two refusals
    // are about ONE rule, and `ReconciliationRowDto` has PlanId but no RuleId, so
    // `/plans/:planId/rules/:ruleId` is unbuildable from what the row holds. Widening the DTO would
    // make the link exact; saying it in words costs nothing and resolves the case now. The plan
    // detail lists its rules, so a reader who arrives knowing "a ladder in this plan refused to
    // price the sale" is one read away from the rule.
    case 'NoMatchingBracket':
    case 'AmountOutsideTable':
      return row.planId
        ? {
            kind: 'link',
            commands: ['/plans', row.planId],
            labelKey: 'RECONCILIATION.RESOLVE.REVIEW_RULES',
            titleKey: 'RECONCILIATION.RESOLVE.REVIEW_RULES_HELP',
          }
        : null;

    // ★ THE PLAN DETAIL IS WHERE CLONING LIVES, which is the documented cure for a plan whose every
    // rule was stopped: an Active plan's rules cannot be revived in place.
    case 'PlanHasNoActiveRules':
      return row.planId
        ? {
            kind: 'link',
            commands: ['/plans', row.planId],
            labelKey: 'RECONCILIATION.RESOLVE.CLONE_PLAN',
          }
        : null;

    // NoQuotaInEffect and CurrencyMismatch fall through here on purpose — see WITHOUT_CURE_SCREEN.
    // So does any code this build has never heard of.
    default:
      return null;
  }
}
