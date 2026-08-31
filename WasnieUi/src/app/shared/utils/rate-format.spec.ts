import {
  formatAmountBound, formatAmountTier, formatAttainmentBound, formatAttainmentTier,
  formatRate, formatRatePercent, isPerUnitRate,
} from './rate-format';

/**
 * How a stored rate is written for a human.
 *
 * ★★ THE REPORTED CASE. A payout statement showed `v1 500% flat` for a rule paying **€5.00 per unit**.
 * The calculation was right all along — the line paid €5.00 for one unit — but the sentence describing
 * it was false, and "500%" on a document about somebody's pay is not a cosmetic defect.
 *
 * ★ THE CAUSE WAS AN ASSUMPTION, NOT A CALCULATION. A rate is stored as a bare decimal; what it means
 * depends on what it applies to. Every screen assumed "amount", multiplied by 100 and appended "%".
 * `0.05` → "5%" is correct under that assumption; `5.00` → "500%" is what happens when it is wrong.
 */
describe('formatRate — a rate means what it is applied to', () => {
  const EN = 'en-US';
  const PER_UNIT = 'per unit';

  // ══ ★ The case that was wrong ═════════════════════════════════════════════

  it('★★ 5.00 against a QUANTITY is five euros per unit — never 500%', () => {
    const result = formatRate(5, 'TransactionQuantity', 'EUR', EN, PER_UNIT);

    expect(result).not.toContain('%', 'the whole bug in one assertion');
    expect(result).toContain('5');
    expect(result).toContain(PER_UNIT);
  });

  it('★ it carries the currency, because a bare "5 per unit" is not an amount', () => {
    const eur = formatRate(5, 'TransactionQuantity', 'EUR', EN, PER_UNIT);
    const usd = formatRate(5, 'TransactionQuantity', 'USD', EN, PER_UNIT);

    expect(eur).not.toEqual(usd);
    expect(eur).toContain('€');
    expect(usd).toContain('$');
  });

  it('a per-unit rate with decimals keeps them', () => {
    expect(formatRate(2.5, 'TransactionQuantity', 'EUR', EN, PER_UNIT)).toContain('2.50');
  });

  // ══ The case that was right, and must stay right ══════════════════════════

  it('★ 0.05 against an AMOUNT is still exactly "5%"', () => {
    // ★ NOT ONE CHARACTER DIFFERENT FROM BEFORE. This work item is about a case that was wrong; a
    // rewrite that also restyles the case that was right is how a fix becomes a regression.
    expect(formatRate(0.05, 'TransactionAmount', 'EUR', EN, PER_UNIT)).toBe('5%');
  });

  it('trailing zeros are still trimmed the old way', () => {
    expect(formatRate(0.1, 'TransactionAmount', 'EUR', EN, PER_UNIT)).toBe('10%');
    expect(formatRate(0.125, 'TransactionAmount', 'EUR', EN, PER_UNIT)).toBe('12.5%');
    expect(formatRate(0.1234, 'TransactionAmount', 'EUR', EN, PER_UNIT)).toBe('12.34%');
  });

  // ══ ★ The unknown case ════════════════════════════════════════════════════

  it('★ an ABSENT base is treated as a percentage, deliberately', () => {
    // ★ THE FAILURE MODES ARE NOT SYMMETRICAL. Guessing "percentage" for a per-unit rate reproduces the
    // display bug we already had and already know how to spot. Guessing "per unit" for a genuine
    // percentage would put a currency symbol on a proportion — a NEW lie, on a screen nobody is
    // watching for it. So the unknown case falls to the shape almost every rule actually has, which is
    // also what an older backend that does not send this field yet means.
    expect(formatRate(0.05, undefined, 'EUR', EN, PER_UNIT)).toBe('5%');
    expect(formatRate(0.05, null, 'EUR', EN, PER_UNIT)).toBe('5%');
    expect(formatRate(0.05, 'SomethingNew', 'EUR', EN, PER_UNIT)).toBe('5%');
  });

  it('isPerUnitRate answers only for the quantity base', () => {
    expect(isPerUnitRate('TransactionQuantity')).toBeTrue();
    expect(isPerUnitRate('TransactionAmount')).toBeFalse();
    expect(isPerUnitRate(undefined)).toBeFalse();
  });

  // ══ Locale ════════════════════════════════════════════════════════════════

  it('the amount is written the way the reader writes numbers', () => {
    // ★ A FIVE-DIGIT NUMBER, because Spanish does NOT group four-digit ones — 1234,50 is correct and
    // has no thousands separator to assert. Picked after the first version of this test failed on
    // exactly that, which is a fact about the locale rather than about the code.
    const es = formatRate(12345.5, 'TransactionQuantity', 'EUR', 'es-ES', 'por unidad');

    expect(es).toContain('por unidad');
    // Grouped with '.', decimated with ',' — asserted on the digits only, because the spacing around
    // the currency symbol is the platform's business.
    expect(es).toMatch(/12\.345,50/);
  });

  it('a missing currency falls back rather than rendering "undefined"', () => {
    const result = formatRate(5, 'TransactionQuantity', null, EN, PER_UNIT);

    expect(result).not.toContain('undefined');
    expect(result).toContain('5');
  });
});

// ── Tier bounds: the second half of the same question ─────────────────────────────────────────

describe('Tier bounds — a bound means what its ladder is measured in', () => {
  const EN = 'en-US';
  const PER_UNIT = 'per unit';
  const QUOTA = '× quota';
  /**
   * ★★ THE REPORTED BUG, PINNED. A payout that had already been PAID showed
   * `0–2000000% @ 4% / 2000000–5000000% @ 6%`. The stored bounds are 0 / 20000 / 50000 — money typed
   * into a ladder whose bounds are ratios of quota — and the screen multiplied them by 100 and
   * appended "%". The data is absurd and stays absurd; what changes is that it stops being described
   * in a unit it was never in.
   */
  it('writes the malformed historical table as ratios of quota, not as percentages', () => {
    const first = formatAttainmentTier(0, 20000, 0.04, EN, QUOTA);
    const second = formatAttainmentTier(20000, 50000, 0.06, EN, QUOTA);

    expect(first).toBe('0–20,000 × quota @ 4%');
    expect(second).toBe('20,000–50,000 × quota @ 6%');

    // The number that was on screen cannot be produced any more.
    expect(`${first} / ${second}`).not.toContain('2000000');
    expect(`${first} / ${second}`).not.toContain('2,000,000');
  });

  it('writes a well-formed attainment ladder the way the rule form does', () => {
    expect(formatAttainmentTier(0, 1, 0.04, EN, QUOTA)).toBe('0–1 × quota @ 4%');
    expect(formatAttainmentTier(1, null, 0.07, EN, QUOTA)).toBe('1+ × quota @ 7%');
    expect(formatAttainmentTier(1, 1.4, 0.07, EN, QUOTA)).toBe('1–1.4 × quota @ 7%');
  });

  /** ★ A Tiered bound is MONEY, and used to print as a bare number with no currency at all. */
  it('writes a Tiered ladder in the plan currency', () => {
    expect(formatAmountTier(0, 1000, 0.05, 'EUR', EN)).toBe('€0.00–€1,000.00 @ 5%');
    expect(formatAmountTier(1000, null, 0.09, 'EUR', EN)).toBe('€1,000.00+ @ 9%');
  });

  it('denominates a Tiered bound in the currency it was given', () => {
    expect(formatAmountTier(0, 1000, 0.05, 'USD', EN)).toContain('$');
    expect(formatAmountTier(0, 1000, 0.05, 'PLN', EN)).toContain('PLN');
  });

  /**
   * ★ IT NEVER INVENTS A UNIT IT CANNOT DETERMINE. Without a currency the figure stays bare: an
   * incomplete label lets the reader recognise the number they typed, while a guessed currency symbol
   * would be a new false statement on a document about money.
   */
  it('falls back to a bare number when no currency is known', () => {
    const text = formatAmountTier(0, 1000, 0.05, null, EN);

    expect(text).toBe('0–1,000 @ 5%');
    expect(text).not.toContain('€');
    expect(text).not.toContain('$');
  });

  it('formats bounds in the reader locale', () => {
    expect(formatAttainmentBound(1.4, 'es-ES')).toBe('1,4');
    expect(formatAttainmentBound(20000, 'es-ES')).toBe('20.000');
    // The currency path is Intl's; assert it follows the locale rather than pinning one
    // engine's grouping choices, which differ by version and by amount.
    expect(formatAmountBound(1000, 'EUR', 'es-ES')).not.toBe(formatAmountBound(1000, 'EUR', EN));
    expect(formatAmountBound(1000, 'EUR', 'es-ES')).toContain('1000,00');
  });

  /** An attainment bound is never a percentage — that is the entire decision this file records. */
  it('never appends a percent sign to a bound', () => {
    expect(formatAttainmentBound(1.4, EN)).not.toContain('%');
    expect(formatAttainmentBound(20000, EN)).not.toContain('%');
    expect(formatAmountBound(20000, 'EUR', EN)).not.toContain('%');
  });

  /** The rate half is unchanged, and is the same function the flat path uses. */
  it('still writes the rate inside a tier as a percentage', () => {
    expect(formatRatePercent(0.04)).toBe('4%');
    expect(formatRatePercent(0.125)).toBe('12.5%');
    expect(formatRate(0.05, 'TransactionAmount', 'EUR', EN, PER_UNIT)).toBe(formatRatePercent(0.05));
  });
});
