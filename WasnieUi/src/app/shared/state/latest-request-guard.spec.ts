import { LatestRequestGuard } from './latest-request-guard';

describe('LatestRequestGuard', () => {
  let guard: LatestRequestGuard;

  beforeEach(() => { guard = new LatestRequestGuard(); });

  it('keeps the response of the only in-flight request', () => {
    const token = guard.begin();
    expect(guard.isStale(token)).toBe(false);
  });

  it('discards a response once a newer request has started', () => {
    const older = guard.begin();
    guard.begin();
    expect(guard.isStale(older)).toBe(true);
  });

  it('keeps the newest request even when an older one is still in flight', () => {
    const older = guard.begin();
    const newer = guard.begin();
    expect(guard.isStale(newer)).toBe(false);
    expect(guard.isStale(older)).toBe(true);
  });

  // The bug this class exists for: the OLDER, wider query is the slow one, so it lands last. Without
  // the guard the caller would write it over the filtered result and the list would look unfiltered.
  it('still discards the older response when it ARRIVES after the newer one', () => {
    const older = guard.begin();
    const newer = guard.begin();

    const arrivals: string[] = [];
    if (!guard.isStale(newer)) arrivals.push('newer');   // newer comes back first
    if (!guard.isStale(older)) arrivals.push('older');   // older comes back last

    expect(arrivals).toEqual(['newer']);
  });

  it('tracks each request independently across many loads', () => {
    const tokens = [guard.begin(), guard.begin(), guard.begin()];
    expect(tokens.map(t => guard.isStale(t))).toEqual([true, true, false]);
  });
});
