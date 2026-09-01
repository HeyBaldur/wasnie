import en from '../../../assets/i18n/en.json';
import es from '../../../assets/i18n/es.json';
import pl from '../../../assets/i18n/pl.json';

/**
 * The archive-plan confirmation, in all three languages.
 *
 * ★★ WHY A TEST FOR A DIALOG. The confirmation used to read "Archiving this plan will make it
 * read-only. This action cannot be undone easily." What archiving actually does is deactivate every
 * active assignment of the plan in the same transaction (ArchivePlanHandler) — it takes every payee
 * off the plan and stops paying them through it. "Read-only" describes a plan that stops being
 * EDITABLE; the user agreed to that and unassigned their sales team (KAN-31).
 *
 * The same failure as the attainment hint, with the sign flipped: there the screen was stricter than
 * the product, here it was gentler. Pinned so the softer wording cannot come back quietly.
 *
 * The bundles are imported directly rather than through the loader: this asserts the FILES are
 * complete, which is what "i18n is done" means. A missing key would otherwise fall back to the key
 * name and look plausible on screen.
 */
describe('Archive plan confirmation — the words, EN / ES / PL', () => {
  const bundles: Record<string, any> = { en, es, pl };
  const langs = ['en', 'es', 'pl'];

  /** The wording that undersold the consequence. Gone from the files, not merely unreferenced. */
  const RETIRED: Record<string, string> = {
    en: 'Archiving this plan will make it read-only. This action cannot be undone easily.',
    es: 'Archivar este plan lo dejará en solo lectura. Esta acción no se puede deshacer fácilmente.',
    pl: 'Zarchiwizowanie planu sprawi, że będzie tylko do odczytu. Tej operacji nie można łatwo cofnąć.',
  };

  /** The consequence each language has to name: the assignments / the payees. */
  const MUST_MENTION: Record<string, string[]> = {
    en: ['assignment'],
    es: ['asignaci'],
    pl: ['przypisan'],
  };

  it('carries both variants in every language', () => {
    for (const lang of langs) {
      expect(bundles[lang]['PLANS']['CONFIRM_ARCHIVE_MSG'])
        .withContext(`${lang}: PLANS.CONFIRM_ARCHIVE_MSG`).toBeTruthy();
      expect(bundles[lang]['PLANS']['CONFIRM_ARCHIVE_MSG_NONE'])
        .withContext(`${lang}: PLANS.CONFIRM_ARCHIVE_MSG_NONE`).toBeTruthy();
    }
  });

  /**
   * ★ THE RETIRED WORDING. The exact sentence that said only "read-only" must not reappear in any
   * language — that is the sentence the user believed.
   */
  it('no longer carries the wording that promised only read-only', () => {
    for (const lang of langs) {
      const msg = bundles[lang]['PLANS']['CONFIRM_ARCHIVE_MSG'];
      expect(msg).withContext(`${lang}: the retired read-only-only wording`).not.toBe(RETIRED[lang]);
    }
  });

  it('names the deactivated assignments, in every language', () => {
    for (const lang of langs) {
      const msg = (bundles[lang]['PLANS']['CONFIRM_ARCHIVE_MSG'] as string).toLowerCase();
      for (const needle of MUST_MENTION[lang]) {
        expect(msg).withContext(`${lang}: must say what is deactivated`).toContain(needle);
      }
    }
  });

  /**
   * ★ THE NUMBER. The counted variant is the one the dialog shows when the plan has assignments, so
   * it has to interpolate; the zero variant must NOT, or the screen would read "0 assignment(s)".
   */
  it('interpolates the count only in the counted variant', () => {
    for (const lang of langs) {
      expect(bundles[lang]['PLANS']['CONFIRM_ARCHIVE_MSG'])
        .withContext(`${lang}: counted variant must carry {{count}}`).toContain('{{count}}');
      expect(bundles[lang]['PLANS']['CONFIRM_ARCHIVE_MSG_NONE'])
        .withContext(`${lang}: zero variant must not carry {{count}}`).not.toContain('{{count}}');
    }
  });
});
