import { buildHttpParams } from './build-http-params';

describe('buildHttpParams', () => {
  it('serializes page and pageSize', () => {
    const p = buildHttpParams({ page: 2, pageSize: 25 });
    expect(p.get('page')).toBe('2');
    expect(p.get('pageSize')).toBe('25');
  });

  it('includes period when provided', () => {
    const p = buildHttpParams({ page: 1, pageSize: 10, period: 'ytd' });
    expect(p.get('period')).toBe('ytd');
  });

  it('omits period when not provided', () => {
    const p = buildHttpParams({ page: 1, pageSize: 10 });
    expect(p.has('period')).toBeFalse();
  });

  it('includes all period values', () => {
    for (const val of ['this-month', 'last-month', 'ytd', 'all-time']) {
      const p = buildHttpParams({ page: 1, pageSize: 10, period: val });
      expect(p.get('period')).toBe(val);
    }
  });

  it('flattens filters into top-level params', () => {
    const p = buildHttpParams({ page: 1, pageSize: 10, filters: { status: 'Active', payeeId: 'abc' } });
    expect(p.get('status')).toBe('Active');
    expect(p.get('payeeId')).toBe('abc');
  });

  it('period and filters coexist', () => {
    const p = buildHttpParams({ page: 1, pageSize: 10, period: 'last-month', filters: { status: 'Active' } });
    expect(p.get('period')).toBe('last-month');
    expect(p.get('status')).toBe('Active');
  });

  it('returns empty params when called with undefined', () => {
    const p = buildHttpParams(undefined);
    expect(p.keys().length).toBe(0);
  });
});
