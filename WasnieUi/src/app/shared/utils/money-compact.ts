/**
 * Compact money, for the figures that lead a screen.
 *
 * ★★ ONE DEFINITION, BECAUSE THERE WERE ALREADY THREE. The dashboard, the payee page and the
 * horizontal bar chart each carry their own `fmtCompact`, identical to this and to each other. That is
 * how two screens meant to show the same number begin to show it differently. This is the floor: new
 * callers use it, and the three copies can migrate one at a time without hurry.
 *
 * ★★ A BLANK CURRENCY IS A REAL STATE, NOT A BUG TO LET THROUGH. When a range holds no money at all
 * there is no currency to name, and Intl throws a RangeError on an empty code — which once took a whole
 * dashboard down rather than showing the zero it was asked for. The figure still has to appear:
 * "nothing here" and "we could not work it out" must never look the same.
 */
export function formatCompactMoney(amount: number, currency: string): string {
  const options: Intl.NumberFormatOptions = currency
    ? { style: 'currency', currency, notation: 'compact', maximumFractionDigits: 2 }
    : { notation: 'compact', maximumFractionDigits: 2 };

  return new Intl.NumberFormat('en-US', options).format(amount);
}
