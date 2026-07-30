import {
  QuotaStatusVariantPipe,
  QuotaStatusLabelPipe,
  QuotaPeriodExpiredPipe,
  isQuotaPeriodExpired,
} from './quota-status.pipe';

describe('Quota status pipes (single source of truth for quota status display)', () => {
  const variant = new QuotaStatusVariantPipe();
  const label = new QuotaStatusLabelPipe();

  it('maps each persisted status to its badge variant', () => {
    expect(variant.transform('Active')).toBe('success');
    expect(variant.transform('Draft')).toBe('neutral');
    expect(variant.transform('Closed')).toBe('warning');
  });

  it('falls back to neutral for unknown/empty status', () => {
    expect(variant.transform('Whatever')).toBe('neutral');
    expect(variant.transform(null)).toBe('neutral');
    expect(variant.transform(undefined)).toBe('neutral');
  });

  it('maps each status to its QUOTAS.STATUS_* i18n key', () => {
    expect(label.transform('Active')).toBe('QUOTAS.STATUS_ACTIVE');
    expect(label.transform('Draft')).toBe('QUOTAS.STATUS_DRAFT');
    expect(label.transform('Closed')).toBe('QUOTAS.STATUS_CLOSED');
  });

  it('does NOT derive status from dates — it is a pure status mapping', () => {
    // A Closed quota whose period is still running must still read "Closed" (the bug this fixes).
    expect(variant.transform('Closed')).toBe('warning');
    expect(label.transform('Closed')).toBe('QUOTAS.STATUS_CLOSED');
  });
});

/**
 * The temporal complement. A quota whose period ended last month still reads "Active", because status
 * moves Draft → Active → Closed by explicit action only — deliberate domain behaviour that nothing
 * here changes. The badge is the missing half of the sentence, and it must appear for exactly one
 * combination and no other.
 */
describe('isQuotaPeriodExpired (Active + period already over)', () => {
  /** A local YYYY-MM-DD offset from today, matching how the API sends a DateOnly. */
  function daysFromToday(days: number): string {
    const d = new Date();
    d.setDate(d.getDate() + days);
    const mm = String(d.getMonth() + 1).padStart(2, '0');
    const dd = String(d.getDate()).padStart(2, '0');
    return `${d.getFullYear()}-${mm}-${dd}`;
  }

  const YESTERDAY = daysFromToday(-1);
  const TODAY = daysFromToday(0);
  const TOMORROW = daysFromToday(1);

  it('flags an Active quota whose period has ended', () => {
    expect(isQuotaPeriodExpired('Active', YESTERDAY)).toBeTrue();
    expect(isQuotaPeriodExpired('Active', '2020-06-30')).toBeTrue();
  });

  it('does NOT flag an Active quota that ends today — the last day still counts', () => {
    expect(isQuotaPeriodExpired('Active', TODAY)).toBeFalse();
  });

  it('does NOT flag an Active quota still running or not yet started', () => {
    expect(isQuotaPeriodExpired('Active', TOMORROW)).toBeFalse();
    expect(isQuotaPeriodExpired('Active', daysFromToday(365))).toBeFalse();
  });

  it('does NOT flag a Draft quota, even with a period long gone', () => {
    // Draft never started counting, so "expired" would be a claim about nothing.
    expect(isQuotaPeriodExpired('Draft', '2020-01-01')).toBeFalse();
  });

  it('does NOT flag a Closed quota — its own badge already says it is over', () => {
    expect(isQuotaPeriodExpired('Closed', '2020-01-01')).toBeFalse();
  });

  it('says nothing when the date is missing or unusable', () => {
    // Better silent than a badge invented from a value nobody can read.
    expect(isQuotaPeriodExpired('Active', null)).toBeFalse();
    expect(isQuotaPeriodExpired('Active', undefined)).toBeFalse();
    expect(isQuotaPeriodExpired('Active', '')).toBeFalse();
    expect(isQuotaPeriodExpired('Active', 'not-a-date')).toBeFalse();
  });

  it('compares by DATE, never by parsed datetime', () => {
    // `new Date('2026-06-30')` is UTC midnight; read back west of Greenwich it lands on the 29th, and
    // the last day of a period would flash the badge for part of the day. Comparing YYYY-MM-DD
    // strings has no timezone to get wrong — proven by the boundary holding on any machine offset.
    expect(isQuotaPeriodExpired('Active', TODAY)).toBeFalse();
    expect(isQuotaPeriodExpired('Active', `${TODAY}T00:00:00`))
      .withContext('an ISO datetime is tolerated, and still compared by its date part')
      .toBeFalse();
    expect(isQuotaPeriodExpired('Active', `${YESTERDAY}T23:59:59`)).toBeTrue();
  });

  it('is exposed as a pipe with the template argument order', () => {
    // Usage: quota.periodEnd | quotaPeriodExpired: quota.status
    const pipe = new QuotaPeriodExpiredPipe();
    expect(pipe.transform(YESTERDAY, 'Active')).toBeTrue();
    expect(pipe.transform(TOMORROW, 'Active')).toBeFalse();
    expect(pipe.transform(YESTERDAY, 'Closed')).toBeFalse();
  });
});
