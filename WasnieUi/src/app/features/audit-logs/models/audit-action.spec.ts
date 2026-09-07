import { actionKey, isKnownAction, resourceLink, UNKNOWN_ACTION_KEY } from './audit-action';

/**
 * The whitelist that stands between the audit log and the reader.
 *
 * The rules it exists to keep are all negative: a key is never CONCATENATED, an unknown code never
 * reaches the screen, and a resource type with no route never becomes a link. Each of those failures
 * is silent in the browser — a raw identifier looks like a label, a dead link looks like a link — so
 * they are pinned here rather than left to be noticed.
 */
describe('audit-action', () => {
  describe('actionKey', () => {
    it('maps a known code to its own key', () => {
      expect(actionKey('PLAN_ARCHIVED')).toBe('AUDIT.ACTION.PLAN_ARCHIVED');
    });

    /**
     * ★★ THE REGRESSION THIS FILE EXISTS FOR. With `'AUDIT.ACTION.' + code` this expectation would
     * pass by ACCIDENT for every known code and then leak `SOMETHING_NEW` at a user the first time
     * the server emitted a code this build had not heard of. Only the unknown case can tell the two
     * implementations apart, which is why it is asserted first among the negatives.
     */
    it('falls back to the generic label for a code it has never heard of', () => {
      expect(actionKey('SOMETHING_NOBODY_HAS_TRANSLATED')).toBe(UNKNOWN_ACTION_KEY);
      expect(actionKey('SOMETHING_NOBODY_HAS_TRANSLATED')).not.toContain('SOMETHING');
    });

    it('falls back for null, undefined and the empty string', () => {
      expect(actionKey(null)).toBe(UNKNOWN_ACTION_KEY);
      expect(actionKey(undefined)).toBe(UNKNOWN_ACTION_KEY);
      expect(actionKey('')).toBe(UNKNOWN_ACTION_KEY);
    });

    /**
     * ★★ THE LOG HOLDS TWO SPELLINGS OF THE SAME ACTIONS (KAN-19 Paso 0). `transaction_voided` has no
     * constant at all and is still being written; `deal_lost_commission_reverted` coexists with its
     * upper-case twin — 7 rows against 3 in the reference tenant. A reader must not see two different
     * sentences for one thing, so both spellings resolve to the SAME key. If somebody "cleans up"
     * those two map entries before the writers are fixed, this goes red.
     */
    it('reads the log\'s two lower-case spellings as their upper-case twins', () => {
      expect(actionKey('transaction_voided')).toBe(actionKey('TRANSACTION_VOIDED'));
      expect(actionKey('deal_lost_commission_reverted'))
        .toBe(actionKey('DEAL_LOST_COMMISSION_REVERTED'));
    });

    it('does not treat an arbitrary case variant as known', () => {
      expect(actionKey('Plan_Archived')).toBe(UNKNOWN_ACTION_KEY);
    });

    /**
     * ★ INHERITED OBJECT PROPERTIES ARE NOT ACTION CODES. A plain-object lookup answers `toString`
     * and `constructor` from the prototype, so a log row carrying either would render whatever that
     * resolved to instead of the generic label.
     */
    it('does not resolve inherited object properties as codes', () => {
      expect(actionKey('toString')).toBe(UNKNOWN_ACTION_KEY);
      expect(actionKey('constructor')).toBe(UNKNOWN_ACTION_KEY);
      expect(isKnownAction('toString')).toBe(false);
    });
  });

  describe('resourceLink', () => {
    it('points a payee row at that payee', () => {
      expect(resourceLink('Payee', 'p-1')).toEqual(['/payees', 'p-1']);
    });

    /**
     * ★★ NO ROUTE MEANS NO LINK, NEVER A GUESSED ONE. `Auth` is a sign-in: there is no entity to
     * open. The same rule KAN-49 settled for the Reconciliation Centre's Resolve column — a row with
     * no destination shows nothing rather than a link that lands on a not-found.
     */
    it('gives no link to resource types that have no screen', () => {
      for (const type of ['Auth', 'Subscription', 'Integration', 'FieldRequirement', 'Reconciliation', 'Credit']) {
        expect(resourceLink(type, 'x-1')).withContext(type).toBeNull();
      }
    });

    it('gives no link when the id is missing, whatever the type', () => {
      expect(resourceLink('Payee', null)).toBeNull();
      expect(resourceLink('Payee', '')).toBeNull();
      expect(resourceLink(null, 'p-1')).toBeNull();
    });

    it('does not resolve inherited object properties as resource types', () => {
      expect(resourceLink('constructor', 'x-1')).toBeNull();
    });
  });
});
