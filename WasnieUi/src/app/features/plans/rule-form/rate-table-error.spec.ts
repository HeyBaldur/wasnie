import { TranslateService } from '@ngx-translate/core';
import { TestBed } from '@angular/core/testing';
import { TranslateModule } from '@ngx-translate/core';
import { HttpErrorResponse } from '@angular/common/http';
import en from '../../../../assets/i18n/en.json';
import es from '../../../../assets/i18n/es.json';
import pl from '../../../../assets/i18n/pl.json';
import { extractApiErrorCode } from '../../../shared/utils/api-error';
import {
  RATE_TABLE_ERR_UNKNOWN,
  isKnownRateTableError,
  rateTableErrorKey,
  rateTableErrorParams,
} from './rate-table-error';

/**
 * The six ladder refusals, from the code the engine emits to the sentence the reader gets.
 *
 * ★★ WHAT THIS PROTECTS. These messages used to be English prose built in C# and painted into the
 * toast unchanged, so the Spanish and Polish builds showed English. Three things can quietly undo
 * that fix, and each has a test here: a code the front end stops recognising (→ the generic line, and
 * never the raw identifier), a translation that goes missing in one language only, and a placeholder
 * that is left unfilled so the reader is told a tier "ends at {{endsAt}}".
 */
describe('Rate-table refusals — code → key → sentence', () => {
  /** Every code the engine can emit, with a representative payload. */
  const CASES: { code: string; params: Record<string, unknown>; key: string }[] = [
    {
      code: 'RateTableEmpty',
      params: {},
      key: 'PLANS.RATE_TABLE_ERR_EMPTY',
    },
    {
      code: 'RateTableTiersOutOfOrder',
      params: { tierNumber: 1, nextTierNumber: 2, startsAt: 5000, nextStartsAt: 0 },
      key: 'PLANS.RATE_TABLE_ERR_OUT_OF_ORDER',
    },
    {
      code: 'RateTableNonLastTierMustBeClosed',
      params: { tierNumber: 1 },
      key: 'PLANS.RATE_TABLE_ERR_NON_LAST_OPEN',
    },
    {
      code: 'RateTableLastTierMustBeOpen',
      params: { tierNumber: 2, endsAt: 10000, bound: 'Amount' },
      key: 'PLANS.RATE_TABLE_ERR_LAST_BOUNDED_AMOUNT',
    },
    {
      code: 'RateTableLastTierMustBeOpen',
      params: { tierNumber: 2, endsAt: 7500, bound: 'AttainmentRatio' },
      key: 'PLANS.RATE_TABLE_ERR_LAST_BOUNDED_RATIO',
    },
    {
      code: 'RateTableTiersOverlap',
      params: { tierNumber: 1, nextTierNumber: 2, endsAt: 100, nextStartsAt: 80 },
      key: 'PLANS.RATE_TABLE_ERR_OVERLAP',
    },
    {
      code: 'RateTableTiersLeaveGap',
      params: { tierNumber: 1, nextTierNumber: 2, endsAt: 0.7999, nextStartsAt: 0.8, bound: 'Amount' },
      key: 'PLANS.RATE_TABLE_ERR_GAP_AMOUNT',
    },
    {
      code: 'RateTableTiersLeaveGap',
      params: { tierNumber: 1, nextTierNumber: 2, endsAt: 0.99, nextStartsAt: 1, bound: 'AttainmentRatio' },
      key: 'PLANS.RATE_TABLE_ERR_GAP_RATIO',
    },
  ];

  // ── The whitelist ─────────────────────────────────────────────────────────────────────────

  for (const c of CASES) {
    it(`maps ${c.code} (${c.params['bound'] ?? 'no bound'}) to ${c.key}`, () => {
      expect(rateTableErrorKey({ code: c.code, parameters: c.params })).toBe(c.key);
    });
  }

  /**
   * ★★ THE RULE THE WHOLE FILE EXISTS FOR. Building `PLANS.RATE_TABLE_ERR_${code}` would print an
   * internal identifier the first time the backend adds an invariant this build has no translation
   * for. The generic line must win, and the code must appear nowhere in what the reader sees.
   */
  it('sends an unrecognised code to the generic line, and never prints the code', () => {
    const invented = { code: 'RateTableTiersHaveBadVibes', parameters: { tierNumber: 3 } };

    expect(rateTableErrorKey(invented)).toBe(RATE_TABLE_ERR_UNKNOWN);
    expect(isKnownRateTableError(invented)).toBe(false);
    expect(rateTableErrorKey(invented)).not.toContain(invented.code);
  });

  it('does not treat a coded error from some other endpoint as a rate-table problem', () => {
    expect(isKnownRateTableError({ code: 'AccountSnapshotStale', parameters: {} })).toBe(false);
  });

  /** `bound` picked the key; leaving it in would offer the sentence a placeholder it never uses. */
  it('drops the bound discriminator from the interpolated values', () => {
    const params = rateTableErrorParams({
      code: 'RateTableTiersLeaveGap',
      parameters: { tierNumber: 1, nextTierNumber: 2, endsAt: 5, nextStartsAt: 7, bound: 'Amount' },
    });

    expect(params['bound']).toBeUndefined();
    expect(params['tierNumber']).toBe(1);
    expect(params['nextStartsAt']).toBe(7);
  });

  // ── Reading the wire ──────────────────────────────────────────────────────────────────────

  it('reads code and parameters out of a coded 422', () => {
    const err = new HttpErrorResponse({
      status: 422,
      error: { status: 422, code: 'RateTableTiersOverlap', parameters: { tierNumber: 1, endsAt: 100 } },
    });

    const coded = extractApiErrorCode(err);

    expect(coded?.code).toBe('RateTableTiersOverlap');
    expect(coded?.parameters['endsAt']).toBe(100);
  });

  it('returns null for the plain message-only error shape, so the caller keeps its old path', () => {
    const err = new HttpErrorResponse({ status: 422, error: { message: 'Something in prose.' } });

    expect(extractApiErrorCode(err)).toBeNull();
  });

  // ── The three bundles ─────────────────────────────────────────────────────────────────────
  //
  // Imported directly rather than through the loader: this asserts the FILES are complete, which is
  // what "i18n is done" means. A missing key falls back to the key name and looks plausible on
  // screen, so only reading the bundle catches it.

  const bundles: Record<string, Record<string, Record<string, string>>> = {
    en: en as never,
    es: es as never,
    pl: pl as never,
  };
  const langs = ['en', 'es', 'pl'];

  const allKeys = [...new Set(CASES.map((c) => c.key)), RATE_TABLE_ERR_UNKNOWN];

  for (const lang of langs) {
    it(`has every rate-table refusal, and the generic fallback, in ${lang}`, () => {
      for (const key of allKeys) {
        const [, leaf] = key.split('.');
        expect(bundles[lang]['PLANS'][leaf]).withContext(`${lang}: ${key}`).toBeTruthy();
      }
    });
  }

  /**
   * ★ EVERY CODE RENDERS A REAL SENTENCE IN EVERY LANGUAGE, WITH ITS NUMBERS IN IT. Asserted through
   * the translate service rather than by comparing strings: what matters is that nothing is left
   * unfilled and that the key itself never reaches the screen, not the exact wording — which is
   * precisely the thing this design made editable without a redeploy.
   */
  for (const lang of langs) {
    it(`renders every refusal in ${lang} with its values interpolated`, () => {
      TestBed.resetTestingModule();
      TestBed.configureTestingModule({ imports: [TranslateModule.forRoot()] });

      const translate = TestBed.inject(TranslateService);
      translate.setTranslation(lang, bundles[lang]);
      translate.use(lang);

      for (const c of CASES) {
        const error = { code: c.code, parameters: c.params };
        const text = translate.instant(rateTableErrorKey(error), rateTableErrorParams(error));

        expect(text).withContext(`${lang}: ${c.key} must resolve`).not.toBe(c.key);
        expect(text).withContext(`${lang}: ${c.key} left a placeholder unfilled`).not.toContain('{{');
        expect(text).withContext(`${lang}: ${c.key} leaked the code`).not.toContain(c.code);

        // The numbers the reader needs in order to find the tier are actually in the sentence.
        for (const [name, value] of Object.entries(rateTableErrorParams(error))) {
          expect(text)
            .withContext(`${lang}: ${c.key} omits ${name}`)
            .toContain(String(value));
        }
      }
    });
  }

  /**
   * ★ THE GENERIC LINE MUST STILL SAY SOMETHING USEFUL. It is what an unrecognised code degrades to,
   * and a fallback that only says "error" would put the reader back where the untranslated prose left
   * them.
   */
  for (const lang of langs) {
    it(`gives the unknown-code fallback real advice in ${lang}`, () => {
      const [, leaf] = RATE_TABLE_ERR_UNKNOWN.split('.');
      expect((bundles[lang]['PLANS'][leaf] || '').length)
        .withContext(`${lang}: ${RATE_TABLE_ERR_UNKNOWN}`)
        .toBeGreaterThan(60);
    });
  }
});
