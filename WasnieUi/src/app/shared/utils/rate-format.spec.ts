import { formatRate, isPerUnitRate } from './rate-format';

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
