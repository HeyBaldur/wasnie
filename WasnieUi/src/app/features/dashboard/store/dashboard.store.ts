import { computed, effect, inject, Injectable, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { DashboardService } from '../services/dashboard.service';
import { DashboardSummary } from '../models/dashboard.models';
import { RefreshableStore } from '../../../shared/state/refreshable-store';

/** An inclusive [from, to] window, both ISO yyyy-MM-dd. */
export interface DashboardRange {
  from: string;
  to: string;
}

/** Local-calendar ISO date. `toISOString()` is NOT usable here: it converts to UTC first, so a user
 *  east of Greenwich picking the 1st gets the 31st of the previous month. */
export function toIsoDate(d: Date): string {
  const month = `${d.getMonth() + 1}`.padStart(2, '0');
  const day = `${d.getDate()}`.padStart(2, '0');
  return `${d.getFullYear()}-${month}-${day}`;
}

/** First to LAST day of the current month — the range the dashboard opens on. */
export function currentMonthRange(today: Date = new Date()): DashboardRange {
  const first = new Date(today.getFullYear(), today.getMonth(), 1);
  // Day 0 of the next month is the last day of this one, leap years included.
  const last = new Date(today.getFullYear(), today.getMonth() + 1, 0);
  return { from: toIsoDate(first), to: toIsoDate(last) };
}

@Injectable({ providedIn: 'root' })
export class DashboardStore implements RefreshableStore {
  private readonly api = inject(DashboardService);

  /**
   * The range that governs the page. Defaults to the WHOLE current month — first to last day, not
   * "the month so far" — which is what the server would apply if asked for nothing.
   *
   * ★ ONE SIGNAL, NOT TWO. A separate `from` and `to` would fire the reload effect twice while the
   * user moved the range, and the intermediate state is a window nobody asked for — briefly a
   * backwards one, which the server refuses.
   */
  readonly range = signal<DashboardRange>(currentMonthRange());
  readonly loading = signal(false);
  readonly error = signal<string | null>(null);
  readonly summary = signal<DashboardSummary | null>(null);

  readonly actionBand = computed(() => this.summary()?.actionBand ?? null);
  readonly periodBand = computed(() => this.summary()?.periodBand ?? null);
  readonly commissionsBand = computed(() => this.summary()?.commissionsBand ?? null);
  readonly trendBand = computed(() => this.summary()?.trendBand ?? null);
  readonly activityFeed = computed(() => this.summary()?.activityFeed ?? []);

  readonly hasPendingActions = computed(() => {
    const b = this.actionBand();
    if (!b) return false;
    return (
      b.draftPayRunsCount > 0 ||
      b.payoutsPendingApprovalCount > 0 ||
      b.payoutsApprovedUnpaidByCurrency.length > 0 ||
      b.pendingByPlanItems.length > 0
    );
  });

  constructor() {
    effect(() => {
      const r = this.range();
      void this._load(r);
    });
  }

  /** Ignores a backwards range rather than asking the server for one it will refuse. */
  setRange(range: DashboardRange): void {
    if (range.to < range.from) return;
    this.range.set(range);
  }

  async reload(): Promise<void> {
    await this._load(this.range());
  }

  /** RefreshableStore — re-fetch with the current range when the dashboard route is re-entered. */
  refresh(): Promise<void> {
    return this.reload();
  }

  private async _load(range: DashboardRange): Promise<void> {
    this.loading.set(true);
    this.error.set(null);
    try {
      const data = await firstValueFrom(this.api.getSummary(range.from, range.to));
      this.summary.set(data);
    } catch {
      this.error.set('ERRORS.GENERIC');
    } finally {
      this.loading.set(false);
    }
  }
}
