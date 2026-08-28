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

  return `${(rate * 100).toFixed(2).replace(/\.?0+$/, '')}%`;
}
