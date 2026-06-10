import { computed, effect, inject, Injectable, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { PayRunsApiService } from '../services/pay-runs.api.service';
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
export class PayRunsStore {
  private readonly api = inject(PayRunsApiService);

  readonly loading = signal(false);
  readonly error = signal<string | null>(null);
  readonly page = signal(1);
  readonly pageSize = signal(25);
  readonly filter = signal<PayRunFilter>({ ...EMPTY_PAY_RUN_FILTER });

  readonly pagedResult = signal<PagedResult<PayRunListItem> | null>(null);
  // Tracks the filter used for the last COMPLETED load — same race-condition guard as PayoutsStore._lastLoadedFilter.
  private readonly _lastLoadedFilter = signal<PayRunFilter | null>(null);

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
    this.loading.set(true);
    this.error.set(null);
    try {
      const filters = PayRunsStore._buildFilterRecord(f);
      const params: PaginationParams = {
        page, pageSize, sortBy: 'periodStart', sortOrder: 'desc',
        filters: Object.keys(filters).length > 0 ? filters : undefined,
      };
      const data = await firstValueFrom(this.api.list(params));
      this.pagedResult.set(data);
      this._lastLoadedFilter.set({ ...f });
    } catch {
      this.error.set('ERRORS.GENERIC');
    } finally {
      this.loading.set(false);
    }
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

  lastLoadedFilter(): PayRunFilter | null { return this._lastLoadedFilter(); }
}
