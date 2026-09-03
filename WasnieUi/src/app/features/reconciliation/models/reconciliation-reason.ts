/**
 * The translation whitelist for reconciliation reason codes.
 *
 * ★★ THE KEY IS NEVER BUILT BY CONCATENATION (§C2). `'RECONCILIATION.REASON.' + code` would look
 * identical and would print a raw internal identifier — `NoMatchingBracket` — on screen the first
 * time the engine emitted a code this build had never heard of. An explicit map cannot do that: an
 * unknown code falls to the generic phrase, which says the truthful thing ("this entry could not be
 * paid, for a reason this version cannot name") instead of leaking a symbol.
 *
 * ★ ADDING A CODE IS A DELIBERATE ACT. A new engine reason appears in the filter (the server serves
 * the list) but renders as the generic label until somebody writes its three translations. That is
 * the correct order: the screen never claims to explain something nobody has worded.
 */
const REASON_KEYS: Readonly<Record<string, string>> = {
  // From the engine's trace, via Credits.RateRefusal (KAN-26 / KAN-28 tanda A)
  NoQuotaInEffect: 'RECONCILIATION.REASON.NO_QUOTA_IN_EFFECT',
  NoMatchingBracket: 'RECONCILIATION.REASON.NO_MATCHING_BRACKET',
  AmountOutsideTable: 'RECONCILIATION.REASON.AMOUNT_OUTSIDE_TABLE',

  // From UnprocessablePendingSpec — the same codes the dashboard card uses
  NoPayee: 'RECONCILIATION.REASON.NO_PAYEE',
  CurrencyMismatch: 'RECONCILIATION.REASON.CURRENCY_MISMATCH',
  NoActiveAssignment: 'RECONCILIATION.REASON.NO_ACTIVE_ASSIGNMENT',

  AmbiguousAttribution: 'RECONCILIATION.REASON.AMBIGUOUS_ATTRIBUTION',
  DealLost: 'RECONCILIATION.REASON.DEAL_LOST',
  CrmDrift: 'RECONCILIATION.REASON.CRM_DRIFT',
  PlanHasNoActiveRules: 'RECONCILIATION.REASON.PLAN_HAS_NO_ACTIVE_RULES',
};

export const UNKNOWN_REASON_KEY = 'RECONCILIATION.REASON.UNKNOWN';

/** The translation key for a reason code, or the generic one. Never the code itself. */
export function reasonKey(code: string | null | undefined): string {
  if (!code) return UNKNOWN_REASON_KEY;
  return REASON_KEYS[code] ?? UNKNOWN_REASON_KEY;
}

export function isKnownReason(code: string | null | undefined): boolean {
  return !!code && code in REASON_KEYS;
}
