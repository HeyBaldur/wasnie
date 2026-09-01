/**
 * How a stored rate is written for a human.
 *
 * ★★ THE BUG THIS EXISTS TO END. A rate is stored as a bare decimal, and what it MEANS depends on what
 * it is applied to: `0.05` against a transaction's AMOUNT is five per cent; `5.00` against its
 * QUANTITY is five euros per unit. Every screen that rendered a rate assumed the first — multiply by
 * 100, append "%" — so a real rule paying €5 per unit appeared on a payout statement as **"500% flat"**.
 * The calculation was right the whole time; only the sentence describing it was false.
 *
 * ★ AND ONE FUNCTION, NOT A FIX PER SCREEN. The same wrong assumption was copied into the payout
 * statement and the credit detail independently. Fixing the reported one and leaving the other is how
 * this bug comes back through a different door next month — which is why every surface now routes
 * through here and the tests assert on this, not on each component.
 */

/** What a rate is applied to. Mirrors the backend's `MeasurementBase`. */
export type RateBase = 'TransactionAmount' | 'TransactionQuantity';

/**
 * True when the rate is an amount of money per unit rather than a proportion.
 *
 * ★ ANYTHING UNRECOGNISED IS TREATED AS A PERCENTAGE, deliberately, because that is the shape almost
 * every rule has and it is what an older backend that does not send this field yet means. The failure
 * mode of guessing wrong in this direction is the display bug we already had; guessing wrong in the
 * other direction would put a currency symbol on a genuine percentage, which is a NEW lie.
 */
export function isPerUnitRate(base: string | null | undefined): boolean {
  return base === 'TransactionQuantity';
}

/**
 * Writes a rate the way its meaning requires.
 *
 * @param rate      the stored decimal, exactly as persisted
 * @param base      what it applies to — see {@link RateBase}
 * @param currency  ISO code, used only for a per-unit rate
 * @param locale    the reader's locale, so 5 shows as "5,00" where that is how numbers are written
 * @param perUnitSuffix already-translated words for "per unit" — this file does no i18n of its own
 *
 * ★ THE PERCENTAGE PATH IS UNCHANGED, TO THE CHARACTER. `0.05` still renders `5%`, trailing zeros
 * trimmed exactly as before. This work item is about a case that was wrong, not an excuse to restyle
 * the case that was right.
 */
export function formatRate(
  rate: number,
  base: string | null | undefined,
  currency: string | null | undefined,
  locale: string,
  perUnitSuffix: string,
): string {
  if (isPerUnitRate(base)) {
    const amount = new Intl.NumberFormat(locale, {
      style: 'currency',
      currency: currency || 'EUR',
    }).format(rate);

    return `${amount} ${perUnitSuffix}`;
  }

  return formatRatePercent(rate);
}

// ── Tier bounds ────────────────────────────────────────────────────────────────────────────────
//
// ★★ THE SECOND HALF OF THE SAME BUG, MISSED THE FIRST TIME. The pass above taught the app that a
// RATE means what it is applied to. It never asked the same question of a tier's BOUNDS, and those
// have the same problem in a worse form: a Tiered ladder's bounds are MONEY, an attainment ladder's
// are a PROPORTION OF QUOTA, and both are stored as bare decimals. The payout breakdown multiplied
// attainment bounds by 100 and appended "%", so a real (malformed) table with bounds of 0–20000
// printed as `0–2000000%`.
//
// ★ AND IT WAS HAND-ROLLED, WHICH IS WHY IT SURVIVED. The percentage string above lives in one
// function that every surface routes through — but the payout breakdown built its own copy inline,
// so fixing the helper never touched it. Both bound shapes now live here for the same reason.

/**
 * A Tiered ladder's bound: MONEY, in the plan's currency.
 *
 * ★ WITHOUT A CURRENCY IT STAYS A BARE NUMBER. A bound printed with the wrong currency symbol is a
 * new lie; a bound printed with none is merely incomplete, and the reader can still recognise the
 * figure they typed.
 */
export function formatAmountBound(
  value: number,
  currency: string | null | undefined,
  locale: string,
): string {
  return currency
    ? new Intl.NumberFormat(locale, { style: 'currency', currency }).format(value)
    : new Intl.NumberFormat(locale, { maximumFractionDigits: 4 }).format(value);
}

/**
 * An attainment ladder's bound: a PROPORTION OF QUOTA, written the way the rule form writes it.
 *
 * ★★ NOT A PERCENTAGE, AND THAT IS THE WHOLE DECISION. The form declares the convention — its
 * columns read "Attain. from (× quota)" and its hint says "1 = 100% of target, 1.4 = 140%" — so a
 * payout that rendered the same stored number as "140%" would show the reader two representations of
 * one value and reproduce the confusion this all came from. The suffix arrives already translated;
 * this file does no i18n of its own.
 */
export function formatAttainmentBound(
  value: number,
  locale: string,
): string {
  return new Intl.NumberFormat(locale, { maximumFractionDigits: 4 }).format(value);
}

/** The percentage half of {@link formatRate}, on its own, for the rate inside a tier row. */
export function formatRatePercent(rate: number): string {
  return `${(rate * 100).toFixed(2).replace(/\.?0+$/, '')}%`;
}

/**
 * One tier row of a Tiered (money) ladder: `€0.00–€1,000.00 @ 5%`.
 *
 * An absent upper bound is the open top tier, written `…+` — it earns its rate on everything above.
 */
export function formatAmountTier(
  from: number,
  to: number | null | undefined,
  rate: number,
  currency: string | null | undefined,
  locale: string,
): string {
  const lo = formatAmountBound(from, currency, locale);
  const range = to != null ? `${lo}–${formatAmountBound(to, currency, locale)}` : `${lo}+`;

  return `${range} @ ${formatRatePercent(rate)}`;
}

/**
 * One tier row of an attainment ladder: `0–1.4 × quota @ 5%`.
 *
 * @param quotaSuffix already-translated "× quota" — the same words the form's column headers use.
 */
export function formatAttainmentTier(
  from: number,
  to: number | null | undefined,
  rate: number,
  locale: string,
  quotaSuffix: string,
): string {
  const lo = formatAttainmentBound(from, locale);
  const range = to != null ? `${lo}–${formatAttainmentBound(to, locale)}` : `${lo}+`;

  return `${range} ${quotaSuffix} @ ${formatRatePercent(rate)}`;
}
