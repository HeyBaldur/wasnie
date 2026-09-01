import en from '../../../../assets/i18n/en.json';
import es from '../../../../assets/i18n/es.json';
import pl from '../../../../assets/i18n/pl.json';

/**
 * The words on the orphan-accounts screen, in all three languages.
 *
 * ★★ WHY A TEST FOR A LABEL. The action column used to read "Close account" in a secondary button —
 * the shape and the words of a mutation — while being a `routerLink` and nothing else. On a screen
 * whose own paragraph said each account "has to be closed deliberately", the promise was explicit and
 * false: somebody would press it expecting to close an account worth €3,869.34 and land on a tab
 * (docs/DIAG_ORPHAN_ACCOUNT_CLOSURE.md).
 *
 * A label that promises an action the product does not have is not a copy problem when the subject is
 * money owed to a person who has left. It is pinned here so it cannot come back quietly.
 *
 * The bundles are imported directly rather than through the loader: this asserts the FILES are
 * complete, which is what "i18n is done" means. A missing key would otherwise fall back to the key
 * name and look fine on screen.
 */
describe('Terminated accounts — the words, EN / ES / PL', () => {
  const bundles: Record<string, any> = { en, es, pl };
  const langs = ['en', 'es', 'pl'];

  it('has the navigation label in every language', () => {
    for (const lang of langs) {
      expect(bundles[lang]['LEDGER']['TERMINATED_VIEW_LEDGER'])
        .withContext(`${lang}: LEDGER.TERMINATED_VIEW_LEDGER`).toBeTruthy();
    }
  });

  /**
   * ★ THE RETIRED KEY. Gone from the files, not merely unreferenced — a dead label that promises a
   * mutation is a trap for whoever reads the bundle next and wires it to something.
   */
  it('no longer carries the label that promised a closure', () => {
    for (const lang of langs) {
      expect(bundles[lang]['LEDGER']['TERMINATED_SETTLE'])
        .withContext(`${lang}: the retired "Close account" label`).toBeUndefined();
    }
  });

  /**
   * ★ AND THE PARAGRAPH HAD THE SAME PROBLEM. It said each account "has to be closed deliberately —
   * settled externally, or written off" on a screen with no closure control. It now says WHERE that
   * happens. Asserted as a property rather than as exact prose: the sentence must point somewhere.
   */
  it('explains where an account is actually closed', () => {
    for (const lang of langs) {
      const intro: string = bundles[lang]['LEDGER']['TERMINATED_INTRO'];
      expect(intro).withContext(`${lang}: LEDGER.TERMINATED_INTRO`).toBeTruthy();
      expect(intro.toLowerCase())
        .withContext(`${lang}: the intro must name the destination, not imply a button here`)
        .toContain('clawback');
    }
  });
});
