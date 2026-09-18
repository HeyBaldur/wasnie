import { computed, inject, Injectable, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { extractApiError } from '../../../shared/utils/api-error';
import { MyDashboardApiService } from '../services/my-dashboard.api.service';
import { MyDashboard, MyCurrencyBalance, MyQuotaAttainment } from '../models/my-dashboard.model';

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

  async load(): Promise<void> {
    this.loading.set(true);
    this.error.set(null);
    try {
      this.data.set(await firstValueFrom(this.api.get()));
    } catch (e) {
      // ★ The stale answer is dropped on purpose. Leaving the previous figures under a failed reload
      // would show money as current that nobody re-read.
      this.data.set(null);
      this.error.set(extractApiError(e));
    } finally {
      this.loading.set(false);
    }
  }

  /** What `<app-shell [refreshOnEnter]>` calls when the screen is entered again. */
  refresh(): void {
    void this.load();
  }
}
