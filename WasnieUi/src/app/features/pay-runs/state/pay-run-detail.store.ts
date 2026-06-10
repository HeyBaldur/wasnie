import { computed, inject, Injectable, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { PayRunsApiService } from '../services/pay-runs.api.service';
import { PayRunDetail } from '../models/pay-run.model';

@Injectable()
export class PayRunDetailStore {
  private readonly api = inject(PayRunsApiService);

  readonly loading = signal(false);
  readonly error = signal<string | null>(null);
  readonly run = signal<PayRunDetail | null>(null);

  // Payouts sub-table pagination state
  readonly page = signal(1);
  readonly pageSize = signal(25);
  readonly excludeZero = signal(true);

  private _runId = '';

  readonly status = computed(() => this.run()?.status ?? null);
  readonly isDraft = computed(() => this.status() === 'Draft');
  readonly isApproved = computed(() => this.status() === 'Approved');
  readonly isPaid = computed(() => this.status() === 'Paid');

  readonly totalAmountsEntries = computed(() =>
    Object.entries(this.run()?.totalAmounts ?? {})
      .map(([currency, amount]) => ({ currency, amount }))
  );

  readonly payoutItems = computed(() => this.run()?.payouts?.items ?? []);
  readonly payoutTotalCount = computed(() => this.run()?.payouts?.totalCount ?? 0);
  readonly payoutTotalPages = computed(() => this.run()?.payouts?.totalPages ?? 1);

  // Summary for mark-paid confirmation modal
  readonly markPaidSummary = computed(() => {
    const r = this.run();
    if (!r) return { count: 0, totalAmounts: [] as { currency: string; amount: number }[], skippedCount: 0 };
    return {
      count: r.paidPayeeCount,
      totalAmounts: Object.entries(r.totalAmounts).map(([currency, amount]) => ({ currency, amount })),
      skippedCount: r.zeroPayoutCount,
    };
  });

  async load(runId: string): Promise<void> {
    this._runId = runId;
    await this._fetch();
  }

  private async _fetch(): Promise<void> {
    this.loading.set(true);
    this.error.set(null);
    try {
      const data = await firstValueFrom(
        this.api.getById(this._runId, this.page(), this.pageSize(), this.excludeZero())
      );
      this.run.set(data);
    } catch {
      this.error.set('ERRORS.GENERIC');
    } finally {
      this.loading.set(false);
    }
  }

  setPage(n: number): void {
    this.page.set(n);
    void this._fetch();
  }

  setExcludeZero(v: boolean): void {
    this.excludeZero.set(v);
    this.page.set(1);
    void this._fetch();
  }

  async reload(): Promise<void> {
    await this._fetch();
  }
}
