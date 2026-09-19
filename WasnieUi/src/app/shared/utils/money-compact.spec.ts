import { formatCompactMoney } from './money-compact';

describe('formatCompactMoney', () => {
  it('renders an amount with its currency, compacted', () => {
    expect(formatCompactMoney(433030, 'EUR')).toBe('€433.03K');
  });

  it('renders zero as a figure, never as a blank', () => {
    expect(formatCompactMoney(0, 'EUR')).toBe('€0');
  });

  it('survives a blank currency instead of throwing', () => {
    // ★ The case that once took a dashboard down: Intl throws a RangeError on an empty code, and a
    // range with no money at all has no currency to name.
    expect(formatCompactMoney(0, '')).toBe('0');
  });
});
