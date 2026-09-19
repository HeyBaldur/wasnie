import { computed, effect, inject, Injectable, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { extractApiError } from '../../../shared/utils/api-error';
import { MyDashboardApiService } from '../services/my-dashboard.api.service';
import { MyDashboard, MyCurrencyBalance, MyQuotaAttainment } from '../models/my-dashboard.model';
import {
  currentMonthRange,
  type DashboardRange,
} from '../../dashboard/store/dashboard.store';

/**
 * KAN-92 — the state behind the personal dashboard.
 *
 * ★★ IT NEVER INVENTS A ZERO. `data` starts null and stays null until the server answers; the screen
 * reads `loading` and `error` and shows neither figures nor an empty state until it knows which one
 * is true. A store that initialised its totals to 0 would paint a correct-looking page of zeros over
 * a failed request — the exact failure this batch exists to remove.
 */
@Injectable({ providedIn: 'root' })
export class MyDashboardStore {
  private readonly api = inject(MyDashboardApiService);

  /**
   * KAN-98 - the window that governs the whole screen. Opens on the WHOLE current month, first to last
   * day, which is what the company dashboard opens on and what the payee page opens on.
   *
   * ONE SIGNAL, NOT TWO. A separate `from` and `to` would fire the reload effect twice while the reader
   * dragged the range, and the state in between is a window nobody asked for - briefly a backwards one,
   * which the server refuses.
   *
   * THE HELPERS COME FROM THE COMPANY DASHBOARD'S STORE, which is where they already live and where
   * `payee-detail` already imports them from. Two screens agreeing on what "this month" means by calling
   * one function is the point; moving the helpers somewhere tidier is a separate change.
   */
  readonly range = signal<DashboardRange>(currentMonthRange());

  readonly loading = signal(false);
  readonly error = signal<string | null>(null);

  private readonly data = signal<MyDashboard | null>(null);

  readonly dashboard = this.data.asReadonly();

  /** Null while unknown — distinct from false, which is a fact the server stated. */
  readonly linked = computed<boolean | null>(() => this.data()?.linked ?? null);

  readonly payeeId = computed<string | null>(() => this.data()?.payeeId ?? null);
  readonly payeeName = computed<string | null>(() => this.data()?.payeeName ?? null);

  readonly balances = computed<MyCurrencyBalance[]>(
    () => this.data()?.summary?.byCurrency ?? [],
  );

  readonly quotas = computed<MyQuotaAttainment[]>(() => this.data()?.quotas ?? []);

  /**
   * ★ The ledger summary can be absent while the person IS linked — the money query refusing is not
   * the same as having no payee, and folding the two would tell somebody they are not in the system
   * because a request timed out.
   */
  readonly hasMoney = computed(() => this.balances().length > 0);

  /**
   * KAN-94. Sales of theirs that cannot become commission until an administrator finishes the setup.
   *
   * ★ IT FALLS BACK TO 0, NOT TO "SHOW THE NOTICE". While the answer is unknown — loading, or a failed
   * request — the honest rendering is nothing at all. Warning somebody that their pay is stuck on the
   * strength of a request that never arrived would be inventing the alarm.
   */
  readonly salesAwaitingSetup = computed(() => this.data()?.salesAwaitingSetup ?? 0);

  /**
   * The window the figures on screen were ACTUALLY built over, as the server reported it - not the one
   * in the picker.
   *
   * THE TWO DIVERGE FOR AS LONG AS A REQUEST IS IN FLIGHT, and that gap is exactly when somebody reads
   * the headline. A heading driven by the control would relabel July's money as August's the instant the
   * range moved, a whole round-trip before the money underneath it changed.
   */
  readonly appliedRange = computed<DashboardRange | null>(() => {
    const d = this.data();
    return d?.from && d?.to ? { from: d.from, to: d.to } : null;
  });

  constructor() {
    effect(() => {
      const r = this.range();
      void this.load(r);
    });
  }

  /** Ignores a backwards range rather than asking the server for one it will refuse. */
  setRange(range: DashboardRange): void {
    if (range.to < range.from) return;
    this.range.set(range);
  }

  async load(range: DashboardRange = this.range()): Promise<void> {
    this.loading.set(true);
    this.error.set(null);
    try {
      this.data.set(await firstValueFrom(this.api.get(range.from, range.to)));
    } catch (e) {
      // ★ The stale answer is dropped on purpose. Leaving the previous figures under a failed reload
      // would show money as current that nobody re-read.
      this.data.set(null);
      this.error.set(extractApiError(e));
    } finally {
      this.loading.set(false);
    }
  }

  /** What `<app-shell [refreshOnEnter]>` calls when the screen is entered again - with the window still
   *  in effect, so coming back does not silently reset what the reader was looking at. */
  refresh(): void {
    void this.load(this.range());
  }
}
