import { Injectable } from '@angular/core';
import { RefreshableStore } from './refreshable-store';

/**
 * Singleton that decides, per store instance, whether entering a route should trigger a re-fetch.
 *
 * The FIRST time a store is seen (first visit to its page in this SPA session) we do NOT refresh — the
 * store's own constructor `effect()` already performs the initial load, so refreshing here would double
 * the request. Every subsequent entry calls `refresh()`, so re-navigation always shows fresh data without
 * a full page reload. Cleared automatically on full reload (the singleton + stores are recreated).
 */
@Injectable({ providedIn: 'root' })
export class RouteRefreshTracker {
  private readonly seen = new WeakSet<RefreshableStore>();

  onEntry(store: RefreshableStore): void {
    if (this.seen.has(store)) {
      void store.refresh();
    } else {
      this.seen.add(store);
    }
  }
}
