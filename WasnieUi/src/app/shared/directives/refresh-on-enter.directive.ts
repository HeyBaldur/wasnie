import { Directive, OnInit, inject, input } from '@angular/core';
import { RefreshableStore } from '../state/refreshable-store';
import { RouteRefreshTracker } from '../state/route-refresh.tracker';

/**
 * Re-fetches a feature store's data whenever its page is (re)entered — the single, shared cure for "stale
 * data after SPA navigation". Apply on the page root (e.g. `<app-shell [refreshOnEnter]="store">`). The
 * directive's `ngOnInit` runs on every mount of the page; the first mount is skipped by RouteRefreshTracker
 * (the store's own initial load covers it), so there is no double-fetch.
 */
@Directive({
  selector: '[refreshOnEnter]',
  standalone: true,
})
export class RefreshOnEnterDirective implements OnInit {
  private readonly tracker = inject(RouteRefreshTracker);

  /** The store to refresh on route entry. Must implement `RefreshableStore`. */
  readonly refreshOnEnter = input.required<RefreshableStore>();

  ngOnInit(): void {
    this.tracker.onEntry(this.refreshOnEnter());
  }
}
