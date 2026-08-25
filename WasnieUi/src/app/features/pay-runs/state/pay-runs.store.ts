import { computed, effect, inject, Injectable, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { PayRunsApiService } from '../services/pay-runs.api.service';
import { LatestRequestGuard } from '../../../shared/state/latest-request-guard';
import { RefreshableStore } from '../../../shared/state/refreshable-store';
import { PayRunListItem, PayRunStatus } from '../models/pay-run.model';
import { PagedResult, PaginationParams } from '../../../shared/models/pagination.models';

export interface PayRunFilter {
  status: PayRunStatus | 'All';
  periodFrom: string | null;
  periodTo: string | null;
}

export const EMPTY_PAY_RUN_FILTER: PayRunFilter = {
  status: 'All',
  periodFrom: null,
  periodTo: null,
};

@Injectable({ providedIn: 'root' })
export class PayRunsStore implements RefreshableStore {
  private readonly api = inject(PayRunsApiService);

  readonly loading = signal(false);
  readonly error = signal<string | null>(null);
  readonly page = signal(1);
  readonly pageSize = signal(10);
  readonly filter = signal<PayRunFilter>({ ...EMPTY_PAY_RUN_FILTER });

  readonly pagedResult = signal<PagedResult<PayRunListItem> | null>(null);
  // Tracks the filter used for the last COMPLETED load. This is an EXPORT concern only — it exists so
  // "Export" ships the predicate the user is actually looking at, never a filter typed a moment ago
  // whose results have not landed yet. It is NOT a race guard (an earlier comment here claimed it was):
  // it never discards a response. Discarding is _latest's job, below.
  private readonly _lastLoadedFilter = signal<PayRunFilter | null>(null);

  // Makes the last-REQUESTED load win rather than the last-ARRIVED one. Without it, switching to
  // status=Draft while the unfiltered load is still in flight left the whole list on screen under a
  // Draft filter until the user reloaded (reproduced 2026-08-18, WI-1 Paso 0.2).
  private readonly _latest = new LatestRequestGuard();

  readonly items = computed(() => this.pagedResult()?.items ?? []);
  readonly totalCount = computed(() => this.pagedResult()?.totalCount ?? 0);
  readonly totalPages = computed(() => this.pagedResult()?.totalPages ?? 1);
  readonly hasNextPage = computed(() => this.pagedResult()?.hasNextPage ?? false);
  readonly hasPreviousPage = computed(() => this.pagedResult()?.hasPreviousPage ?? false);

  constructor() {
    effect(() => {
      void this._loadList(this.page(), this.pageSize(), this.filter());
    });
  }

  static _buildFilterRecord(f: PayRunFilter): Record<string, string> {
    const p: Record<string, string> = {};
    if (f.status !== 'All') p['status'] = f.status;
    if (f.periodFrom) p['periodFrom'] = f.periodFrom;
    if (f.periodTo) p['periodTo'] = f.periodTo;
    return p;
  }

  private async _loadList(page: number, pageSize: number, f: PayRunFilter): Promise<void> {
    const token = this._latest.begin();
    this.loading.set(true);
    this.error.set(null);
    try {
      const filters = PayRunsStore._buildFilterRecord(f);
      const params: PaginationParams = {
        page, pageSize, sortBy: 'createdAt', sortOrder: 'desc',
        filters: Object.keys(filters).length > 0 ? filters : undefined,
      };
      const data = await firstValueFrom(this.api.list(params));
      if (this._latest.isStale(token)) return;   // superseded by a newer load — discard
      this.pagedResult.set(data);
      this._lastLoadedFilter.set({ ...f });
    } catch {
      if (this._latest.isStale(token)) return;   // don't let a stale failure clobber a fresh result
      this.error.set('ERRORS.GENERIC');
    } finally {
      // Only the newest request owns the spinner; a stale one finishing must not clear it while the
      // current load is still running.
      if (!this._latest.isStale(token)) this.loading.set(false);
    }
  }

  readonly activeFilterCount = computed(() => {
    const f = this.filter();
    let n = 0;
    if (f.status !== 'All') n++;
    if (f.periodFrom || f.periodTo) n++;
    return n;
  });

  toExportParams(): Record<string, string> {
    const f = this._lastLoadedFilter() ?? this.filter();
    return PayRunsStore._buildFilterRecord(f);
  }

  setFilter(partial: Partial<PayRunFilter>): void {
    this.filter.update(f => ({ ...f, ...partial }));
    this.page.set(1);
  }

  clearFilters(): void {
    this.filter.set({ ...EMPTY_PAY_RUN_FILTER });
    this.page.set(1);
  }

  setPage(n: number): void { this.page.set(n); }
  setPageSize(n: number): void { this.pageSize.set(n); this.page.set(1); }

  async reload(): Promise<void> {
    await this._loadList(this.page(), this.pageSize(), this.filter());
  }

  /**
   * RefreshableStore — re-fetch the CURRENT page/filter when the route is re-entered, without
   * resetting anything. This store was the only list store missing it, so `/pay-runs` was also the
   * only list that kept showing the previous visit's rows after an SPA navigation back to it.
   */
  refresh(): Promise<void> {
    return this.reload();
  }

  lastLoadedFilter(): PayRunFilter | null { return this._lastLoadedFilter(); }
}
