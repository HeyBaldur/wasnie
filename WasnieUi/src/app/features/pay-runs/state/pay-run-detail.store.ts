import { computed, inject, Injectable, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { PayRunsApiService } from '../services/pay-runs.api.service';
import { PayRunDetail, PayRunPayoutsDetailFilter, EMPTY_PAYOUTS_DETAIL_FILTER } from '../models/pay-run.model';

@Injectable()
export class PayRunDetailStore {
  private readonly api = inject(PayRunsApiService);

  readonly loading = signal(false);
  readonly error = signal<string | null>(null);
  readonly run = signal<PayRunDetail | null>(null);

  readonly page = signal(1);
  readonly pageSize = signal(10);

  readonly filter = signal<PayRunPayoutsDetailFilter>({ ...EMPTY_PAYOUTS_DETAIL_FILTER });
  // Race-condition guard: export reads the filter used by the last completed load.
  private readonly _lastLoadedFilter = signal<PayRunPayoutsDetailFilter | null>(null);

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

  readonly excludeZero = computed(() => this.filter().excludeZero);

  readonly activeFilterCount = computed(() => {
    const f = this.filter();
    let n = 0;
    if (f.status) n++;
    if (f.periodFrom || f.periodTo) n++;
    if (f.amountMin != null || f.amountMax != null) n++;
    if (f.payeeIds.length > 0) n++;
    if (f.planIds.length > 0) n++;
    if (f.currencies.length > 0) n++;
    return n;
  });

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
        this.api.getById(this._runId, this.filter(), this.page(), this.pageSize())
      );
      this.run.set(data);
      this._lastLoadedFilter.set({ ...this.filter() });
    } catch {
      this.error.set('ERRORS.GENERIC');
    } finally {
      this.loading.set(false);
    }
  }

  setFilter(partial: Partial<PayRunPayoutsDetailFilter>): void {
    this.filter.update(f => ({ ...f, ...partial }));
    this.page.set(1);
    void this._fetch();
  }

  clearFilters(): void {
    this.filter.set({ ...EMPTY_PAYOUTS_DETAIL_FILTER });
    this.page.set(1);
    void this._fetch();
  }

  setExcludeZero(v: boolean): void {
    this.setFilter({ excludeZero: v });
  }

  setPage(n: number): void {
    this.page.set(n);
    void this._fetch();
  }

  setPageSize(n: number): void {
    this.pageSize.set(n);
    this.page.set(1);
    void this._fetch();
  }

  async reload(): Promise<void> {
    await this._fetch();
  }

  toExportParams(): Record<string, string> {
    const f = this._lastLoadedFilter() ?? this.filter();
    const p: Record<string, string> = {};
    if (f.status) p['status'] = f.status;
    if (f.periodFrom) p['periodFrom'] = f.periodFrom;
    if (f.periodTo) p['periodTo'] = f.periodTo;
    if (f.amountMin != null) p['amountMin'] = String(f.amountMin);
    if (f.amountMax != null) p['amountMax'] = String(f.amountMax);
    if (f.payeeIds.length > 0) p['payeeIds'] = f.payeeIds.join(',');
    if (f.planIds.length > 0) p['planIds'] = f.planIds.join(',');
    if (f.currencies.length > 0) p['currencies'] = f.currencies.join(',');
    if (f.excludeZero) p['excludeZero'] = 'true';
    return p;
  }
}
