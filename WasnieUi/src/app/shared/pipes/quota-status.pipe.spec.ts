import { QuotaStatusVariantPipe, QuotaStatusLabelPipe } from './quota-status.pipe';

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
