import en from '../../../assets/i18n/en.json';
import es from '../../../assets/i18n/es.json';
import pl from '../../../assets/i18n/pl.json';

type Bundle = Record<string, unknown>;

const BUNDLES: Record<string, Bundle> = {
  en: en as unknown as Bundle,
  es: es as unknown as Bundle,
  pl: pl as unknown as Bundle,
};

/** Every leaf key of a bundle, dotted — `USERS.TABLE.NAME`, not the objects on the way there. */
function leafKeys(node: unknown, prefix = ''): string[] {
  if (node === null || typeof node !== 'object' || Array.isArray(node)) return [prefix];

  return Object.entries(node as Bundle).flatMap(([k, v]) =>
    leafKeys(v, prefix ? `${prefix}.${k}` : k),
  );
}

/**
 * KAN-95 — the three translation bundles must describe the same product.
 *
 * ★★ THE DEFECT THAT BOUGHT THIS TEST. The whole AI-token block was pasted INSIDE
 * `FORGOT_PASSWORD` in `en.json`, where its own `TITLE` and `DESC` overwrote the real ones — so the
 * English "Forgot your password?" screen was titled "AI token usage", with a description about Zeke's
 * token consumption, above a working email form. It shipped, and nothing complained: the file is valid
 * JSON, the app compiles, the screen renders, and `SUBMIT`/`SENT_*` survived because they did not
 * collide. Only a human reading that page in English could have caught it.
 *
 * ★★ AND IT WAS INVISIBLE PRECISELY WHERE IT HURT MOST. `en` is the default language AND the fallback
 * for the other two, so a key broken there is broken for everybody. ES and PL were correct throughout
 * — which is the shape this test keys on: the three bundles disagreeing about what keys exist is
 * almost always one of them having been edited by hand into the wrong place.
 *
 * ★ IT COMPARES SHAPE, NEVER WORDS. Whether a Polish sentence is a good translation is not something
 * a unit test can know; whether Polish is MISSING a sentence English has is exactly what it can.
 */
describe('i18n — the three bundles stay in step', () => {
  const keys: Record<string, Set<string>> = {
    en: new Set(leafKeys(BUNDLES['en'])),
    es: new Set(leafKeys(BUNDLES['es'])),
    pl: new Set(leafKeys(BUNDLES['pl'])),
  };

  for (const lang of ['es', 'pl']) {
    it(`${lang} has every key en has`, () => {
      const missing = [...keys['en']].filter(k => !keys[lang].has(k));
      expect(missing).toEqual([]);
    });

    it(`${lang} has no key en lacks`, () => {
      // ★ THE DIRECTION THAT CAUGHT KAN-95. A key present in one bundle and absent from the others is
      // the signature of a block pasted into the wrong object — which is what happened, and the only
      // structural trace it left.
      const extra = [...keys[lang]].filter(k => !keys['en'].has(k));
      expect(extra).toEqual([]);
    });
  }

  /**
   * ★★ THE SCREEN THE DEFECT LANDED ON, NAMED. The parity checks above would pass if somebody pasted
   * the same wrong block into all three bundles, so the one page that actually broke gets an assertion
   * about its CONTENT and not just its shape.
   */
  it('the password-reset screen is about resetting a password', () => {
    for (const lang of ['en', 'es', 'pl']) {
      const screen = (BUNDLES[lang] as Record<string, Record<string, string>>)['FORGOT_PASSWORD'];

      expect(Object.keys(screen).sort())
        .withContext(`${lang} FORGOT_PASSWORD carries keys that are not its own`)
        .toEqual(['DESC', 'SENT_DESC', 'SENT_TITLE', 'SUBMIT', 'TITLE']);

      expect(JSON.stringify(screen).toLowerCase())
        .withContext(`${lang} FORGOT_PASSWORD still mentions tokens`)
        .not.toContain('token');
    }
  });

  /**
   * KAN-96. The brand in user-facing copy is Incentra.
   *
   * ★ THE CODE IS STILL WASNIE ON PURPOSE — namespaces, projects, the database — and that is not what
   * this looks at. These bundles are nothing BUT user-facing text, so the boundary is unambiguous here:
   * the rebranding left two strings behind ("Settled outside Wasnie") and this is what stops the next
   * two.
   */
  it('no bundle shows the user the old brand name', () => {
    for (const lang of ['en', 'es', 'pl']) {
      expect(JSON.stringify(BUNDLES[lang]))
        .withContext(`${lang} still says "Wasnie" in user-facing copy`)
        .not.toContain('Wasnie');
    }
  });
});
