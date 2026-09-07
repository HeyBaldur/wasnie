import { computed, inject, Injectable, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';

/**
 * What the sidebar may show. ★ `null` means "not yours to see", and it is NOT zero — the server sends
 * null for a badge the user lacks the permission for, and the sidebar draws nothing. A 0 is a real
 * count and IS drawn: "the queue is empty" is worth knowing.
 */
export interface SidebarBadges {
  readonly reconciliation: number | null;
  readonly terminatedAccounts: number | null;
  readonly financialsTotal: number;
}

const EMPTY: SidebarBadges = { reconciliation: null, terminatedAccounts: null, financialsTotal: 0 };

/**
 * How often the badges re-check themselves when nothing has happened. Five minutes: these numbers
 * move when somebody else in the tenant works, and a stale badge is a small wrong, while a poll on a
 * short timer is a request every user pays for all day.
 */
const REFRESH_INTERVAL_MS = 5 * 60 * 1000;

/**
 * The counts beside the sidebar links.
 *
 * ★★ IT IS NOT FETCHED ON NAVIGATION. The sidebar renders on every route, and wiring this to the
 * router would turn three small numbers into a request per click. It loads once, refreshes on a slow
 * timer, and is told explicitly by the screens that change a count — see {@link refresh}.
 */
@Injectable({ providedIn: 'root' })
export class SidebarBadgesStore {
  private readonly http = inject(HttpClient);

  private readonly _badges = signal<SidebarBadges>(EMPTY);
  private timer: ReturnType<typeof setInterval> | null = null;
  private started = false;

  readonly badges = this._badges.asReadonly();

  readonly reconciliation = computed(() => this._badges().reconciliation);
  readonly terminatedAccounts = computed(() => this._badges().terminatedAccounts);

  /**
   * ★ ZERO IS NOT WORTH A BADGE ON A GROUP. The Financials row is a container: a "0" beside it says
   * nothing the children do not already say, and it would sit there permanently on a healthy tenant.
   * The individual links still show their own 0.
   */
  readonly financialsTotal = computed(() => {
    const total = this._badges().financialsTotal;
    return total > 0 ? total : null;
  });

  /**
   * Fetch now.
   *
   * ★ IT NEVER THROWS AND NEVER CLEARS. A badge is decoration on someone else's screen: if the call
   * fails the previous numbers stay, because a sidebar that empties itself on a blip looks like work
   * disappearing. Errors are swallowed on purpose.
   */
  async refresh(): Promise<void> {
    try {
      this._badges.set(await firstValueFrom(this.http.get<SidebarBadges>('/api/sidebar-badges')));
    } catch {
      // Keep whatever we had.
    }
  }

  /**
   * Load once and keep them warm.
   *
   * ★★ IT FETCHES ONLY THE FIRST TIME, AND THAT IS NOT AN OPTIMISATION. The sidebar is rebuilt on
   * every navigation — each of the 41 feature templates renders its own app-shell — so its ngOnInit
   * runs on every click. Refreshing here would be a request per navigation, which is exactly what
   * this feature was asked not to do. The counts stay current through the timer and through the
   * screens that tell us a count changed.
   *
   * ★ Safe to call again: neither the fetch nor the timer is repeated.
   */
  start(): void {
    if (this.started) return;
    this.started = true;

    void this.refresh();
    this.timer = setInterval(() => void this.refresh(), REFRESH_INTERVAL_MS);
  }

  stop(): void {
    this.started = false;
    if (this.timer === null) return;
    clearInterval(this.timer);
    this.timer = null;
  }
}
