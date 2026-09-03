import {
  resolutionFor,
  noResolutionReason,
  noResolutionKey,
  type DeepLinkResolution,
} from './reconciliation-resolution';
import { ReconciliationRow } from './reconciliation.model';

/**
 * KAN-49 Paso 0, as an executable table.
 *
 * ★★ THIS IS THE SAFETY NET THE TICKET ASKED FOR. The mapping lives in a pure function precisely so
 * "which reason has a cure and where does it go" can be asserted without rendering anything — and so
 * the answer for a reason with NO cure is pinned as firmly as the answers for the ones that have one.
 */
function row(overrides: Partial<ReconciliationRow> = {}): ReconciliationRow {
  return {
    kind: 'Transaction',
    entityId: 'e1',
    transactionId: 't1',
    referenceNumber: 'REF-1',
    payeeId: 'p1',
    payeeName: 'Payee One',
    payeeCode: 'EMP1',
    planId: 'pl1',
    planName: 'Plan One',
    amount: 100,
    currency: 'EUR',
    moneyKind: 'AffectedBase',
    periodDate: '2026-05-15',
    occurredAt: '2026-06-01T00:00:00Z',
    reasons: [],
    ...overrides,
  };
}

describe('Reconciliation resolution map (KAN-49 Paso 0)', () => {
  it('sends a payee with no assignment to a pre-filled assignment form', () => {
    const r = resolutionFor(row({ reasons: ['NoActiveAssignment'] }));
    expect(r).toEqual({
      kind: 'link',
      commands: ['/assignments', 'new'],
      queryParams: { payeeId: 'p1' },
      labelKey: 'RECONCILIATION.RESOLVE.CREATE_ASSIGNMENT',
    });
  });

  /**
   * ★ THE LIST, NOT THE DETAIL. The "assign payee" modal lives on the transactions list; the detail
   * screen is read-only. A link to the detail would look right and offer nothing.
   */
  it('sends a sale with no payee to the transactions list filtered to that reference', () => {
    const r = resolutionFor(row({ reasons: ['NoPayee'], payeeId: null }));
    expect(r).toEqual({
      kind: 'link',
      commands: ['/transactions'],
      queryParams: { ref: 'REF-1' },
      labelKey: 'RECONCILIATION.RESOLVE.ASSIGN_PAYEE',
    });
  });

  it('sends ambiguity to the payee’s assignments, which is what has to change', () => {
    const r = resolutionFor(row({ reasons: ['AmbiguousAttribution'] }));
    expect(r).toEqual(jasmine.objectContaining({
      kind: 'link',
      commands: ['/assignments'],
      queryParams: { payeeId: 'p1' },
    }));
  });

  it('sends both ladder refusals and a dead plan to the plan', () => {
    for (const reason of ['NoMatchingBracket', 'AmountOutsideTable', 'PlanHasNoActiveRules']) {
      const r = resolutionFor(row({ kind: 'Credit', reasons: [reason] }));
      expect(r).withContext(reason)
        .toEqual(jasmine.objectContaining({ kind: 'link', commands: ['/plans', 'pl1'] }));
    }
  });

  /**
   * ★★ THE TOOLTIP IS WHAT MAKES THE PARTIAL LINK ENOUGH. The row carries no rule id, so the link
   * can only reach the plan; the explanatory key is how the reader learns WHY they were sent there.
   * Without it the button says "Review plan rules" and leaves them to guess which one and what for.
   */
  it('gives the ladder refusals an explanatory tooltip the plan link alone cannot carry', () => {
    for (const reason of ['NoMatchingBracket', 'AmountOutsideTable']) {
      const r = resolutionFor(row({ kind: 'Credit', reasons: [reason] }));
      expect(r).withContext(reason).toEqual(jasmine.objectContaining({
        labelKey: 'RECONCILIATION.RESOLVE.REVIEW_RULES',
        titleKey: 'RECONCILIATION.RESOLVE.REVIEW_RULES_HELP',
      }));
    }
  });

  /**
   * A destination whose own name says enough carries no separate tooltip key, and the template
   * falls back to the label for it.
   *
   * ★ ASSERTED ON THE PROPERTY, NOT WITH `objectContaining({ titleKey: undefined })` — that form
   * requires the key to be PRESENT and undefined, while the resolution simply omits it, so it went
   * red against correct code.
   */
  it('leaves the tooltip key off the links that explain themselves', () => {
    const r = resolutionFor(row({ reasons: ['PlanHasNoActiveRules'] })) as DeepLinkResolution;
    expect(r.kind).toBe('link');
    expect(r.titleKey).toBeUndefined();
  });

  // ══ Creating the missing quota ════════════════════════════════════════════════════════════

  /**
   * ★★ THE CURE IS TO CREATE A QUOTA, NOT TO GO LOOK AT THE PAYEE'S QUOTAS. Paso 0 first recorded
   * this reason as having no destination because it went looking for the screen the ticket named —
   * "quotas of this payee" — which does not exist. `/quotas/new` always did, and it pre-fills.
   */
  it('sends a missing quota to the quota form, pre-filled with the payee', () => {
    const r = resolutionFor(row({ kind: 'Credit', reasons: ['NoQuotaInEffect'] }));
    expect(r).toEqual({
      kind: 'link',
      commands: ['/quotas', 'new'],
      queryParams: { payeeId: 'p1' },
      labelKey: 'RECONCILIATION.RESOLVE.CREATE_QUOTA',
    });
  });

  /**
   * ★ THE ONE PLACE A MISSING ID DEGRADES THE ACTION INSTEAD OF REMOVING IT. Creating a quota is
   * the cure either way; the pre-fill is a convenience, not the reason to navigate.
   */
  it('still offers the quota form when the row has no payee to pre-fill', () => {
    const r = resolutionFor(row({ kind: 'Credit', reasons: ['NoQuotaInEffect'], payeeId: null }));
    expect(r).toEqual(jasmine.objectContaining({
      commands: ['/quotas', 'new'],
      queryParams: undefined,
    }));
  });

  // ══ The action that actually runs something ═══════════════════════════════════════════════

  /**
   * ★ START AND END ARE THE ROW'S OWN DATE — the narrowest scope that still reaches this sale, and
   * the shape `ByPayeeAndPeriod` is validated to require server-side.
   */
  it('turns a payable-but-unpaid sale into a reprocess scoped to its payee and date', () => {
    const r = resolutionFor(row({ reasons: ['ProcessableWithoutCredit'] }));
    expect(r).toEqual({
      kind: 'reprocess',
      payeeId: 'p1',
      periodStart: '2026-05-15',
      periodEnd: '2026-05-15',
      labelKey: 'RECONCILIATION.RESOLVE.REPROCESS',
    });
  });

  it('offers no reprocess when the row lacks the payee or the date the job requires', () => {
    expect(resolutionFor(row({ reasons: ['ProcessableWithoutCredit'], payeeId: null }))).toBeNull();
    expect(resolutionFor(row({ reasons: ['ProcessableWithoutCredit'], periodDate: null }))).toBeNull();
  });

  // ══ The reasons with no cure — the point of Paso 0 ════════════════════════════════════════

  /**
   * ★★ THE RULE SURVIVES ITS OWN CORRECTION, AND THAT IS WHY THIS TEST STILL EXISTS.
   * `NoQuotaInEffect` used to be asserted here and was wrong — its cure screen was `/quotas/new` all
   * along. `CurrencyMismatch` is the real case: what fixing it even MEANS is KAN-45's decision
   * (re-denominate the plan, or route the sale elsewhere), and those are different screens. A link
   * now would be a guess, so it gets no button — never a deep link that goes somewhere unhelpful.
   */
  it('offers nothing for a reason whose cure screen does not exist yet', () => {
    const r = row({ kind: 'Credit', reasons: ['CurrencyMismatch'] });
    expect(resolutionFor(r)).toBeNull();
    expect(noResolutionReason(r)).toBe('NO_CURE_SCREEN');
  });

  it('distinguishes "no cure screen" from "this row lacks identifiers"', () => {
    const noIds = row({ reasons: ['NoActiveAssignment'], payeeId: null });
    expect(resolutionFor(noIds)).toBeNull();
    expect(noResolutionReason(noIds)).toBe('ROW_LACKS_IDENTIFIERS');
  });

  /** ★ §C2 again: the tooltip key is looked up, never assembled from the enum member. */
  it('returns a spelled-out key for each no-resolution case', () => {
    expect(noResolutionKey(row({ reasons: ['CurrencyMismatch'] })))
      .toBe('RECONCILIATION.RESOLVE.NONE_NO_CURE_SCREEN');
    expect(noResolutionKey(row({ reasons: ['NoActiveAssignment'], payeeId: null })))
      .toBe('RECONCILIATION.RESOLVE.NONE_ROW_LACKS_IDENTIFIERS');
  });

  it('never invents an action for a code this build has never heard of', () => {
    expect(resolutionFor(row({ reasons: ['SomeFutureEngineReason'] }))).toBeNull();
  });

  // ══ One row, many reasons ════════════════════════════════════════════════════════════════

  /**
   * ★ THE FIRST MAPPED REASON WINS, and an unmapped one does not block the mapped one behind it. A
   * row carrying both an unfixable reason and a fixable one must still offer the fix.
   */
  it('offers the first action available across all of a row’s reasons', () => {
    const r = resolutionFor(row({ reasons: ['CurrencyMismatch', 'NoActiveAssignment'] }));
    expect(r).toEqual(jasmine.objectContaining({ commands: ['/assignments', 'new'] }));
  });
});
