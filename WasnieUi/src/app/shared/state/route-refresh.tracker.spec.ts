import { RouteRefreshTracker } from './route-refresh.tracker';
import { RefreshableStore } from './refreshable-store';

describe('RouteRefreshTracker', () => {
  function makeStore(): RefreshableStore & { refresh: jasmine.Spy } {
    return { refresh: jasmine.createSpy('refresh') };
  }

  it('does NOT refresh on the first entry (the store loads itself initially)', () => {
    const tracker = new RouteRefreshTracker();
    const store = makeStore();

    tracker.onEntry(store);

    expect(store.refresh).not.toHaveBeenCalled();
  });

  it('refreshes on every subsequent entry of the same store', () => {
    const tracker = new RouteRefreshTracker();
    const store = makeStore();

    tracker.onEntry(store); // first: skip
    tracker.onEntry(store); // re-entry: refresh
    tracker.onEntry(store); // re-entry: refresh

    expect(store.refresh).toHaveBeenCalledTimes(2);
  });

  it('tracks each store independently (first entry of a different store is still skipped)', () => {
    const tracker = new RouteRefreshTracker();
    const a = makeStore();
    const b = makeStore();

    tracker.onEntry(a); // a first: skip
    tracker.onEntry(b); // b first: skip
    tracker.onEntry(a); // a re-entry: refresh

    expect(a.refresh).toHaveBeenCalledTimes(1);
    expect(b.refresh).not.toHaveBeenCalled();
  });
});
