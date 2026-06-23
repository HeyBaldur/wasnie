/**
 * Contract for per-feature stores that should re-fetch their data when the user (re)enters the route.
 * A singleton store (`providedIn: 'root'`) caches in signals and only fetches on creation / signal change,
 * so on SPA re-navigation it would show stale data. Implementing `refresh()` + applying
 * `[refreshOnEnter]="store"` (see RefreshOnEnterDirective) keeps every page fresh on entry, in ONE place.
 *
 * `refresh()` MUST re-fetch using the store's CURRENT parameters (page / sort / filter / period). It must
 * NOT reset filters. It is only ever called on RE-entry (the first mount is skipped — the store's own
 * initial load handles that — see RouteRefreshTracker), so it does not need to guard against the
 * constructor's initial fetch.
 */
export interface RefreshableStore {
  refresh(): void | Promise<void>;
}
