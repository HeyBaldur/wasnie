import { DestroyRef } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';

/** What a screen does with the filter carried in the URL. */
export interface UrlFilterHandlers {
  /** The URL carries filter params — put them into the store (and the form). */
  apply(qp: Record<string, string>): void;
  /**
   * The URL carries NO filter params — restore this screen's DEFAULT state.
   *
   * Deliberately a callback rather than a fixed `store.clearFilters()`: "no params" does not mean
   * "no filter" on every screen. Pay Runs defaults to `this-month`, so a blind clear would widen the
   * list to all time — a different wrong answer, not a fix. Each screen states its own default here.
   */
  reset(): void;
}

/**
 * Makes a list screen's filter FOLLOW the URL — the shared cure for "the list shows the previous
 * visit's data until I press refresh". Sibling of `RefreshOnEnterDirective`: that one keeps the data
 * fresh on re-entry, this one keeps the FILTER correct.
 *
 * Extracted from `PayoutsListComponent` (commit 4659367), the one screen that already had it right.
 *
 * ## The two bugs this kills
 *
 * **Stale filter on component reuse.** Reading `route.snapshot.queryParams` in `ngOnInit` only works
 * once. Angular's default reuse strategy keeps the component alive when a navigation changes only the
 * query params, so `ngOnInit` never runs again and the snapshot goes stale. Subscribing re-applies on
 * every change.
 *
 * **Leftover filter when arriving with no params.** The stores are `providedIn: 'root'` singletons:
 * they outlive the component and keep the last filter. Guarding the read with `if (params exist)` and
 * no `else` therefore leaves the previous visit's filter applied under a URL that no longer mentions
 * it. `reset()` is called on the empty case for exactly this reason — it is not optional.
 *
 * ## Loop safety — read before using this on a new screen
 *
 * Safe ONLY when the screen writes its own URL with `history.replaceState`, which the router does not
 * observe, so re-applying can never re-trigger itself. Audited 2026-08-18: Transactions, Credits and
 * Payouts use `replaceState` (safe); Pay Runs, Payees, Plans and Quotas do not write filter params at
 * all (safe). `AssignmentsListComponent.clearPayeeFilter` uses `router.navigate(...)`, which the
 * router DOES observe — binding it without converting that call first would let the URL overwrite
 * store state mid-interaction. Convert the write to `replaceState` before binding that screen.
 */
export function bindFiltersToUrl(
  route: ActivatedRoute,
  destroyRef: DestroyRef,
  handlers: UrlFilterHandlers,
): void {
  route.queryParams
    .pipe(takeUntilDestroyed(destroyRef))
    .subscribe(params => {
      const qp = params as Record<string, string>;
      if (Object.keys(qp).length > 0) {
        handlers.apply(qp);
      } else {
        handlers.reset();
      }
    });
}
