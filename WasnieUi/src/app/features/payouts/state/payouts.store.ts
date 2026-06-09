import { computed, effect, inject, Injectable, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { PayoutsApiService } from '../services/payouts.api.service';
import { PayoutListItem, PayoutStatus } from '../models/payout.model';
import { PagedResult, PaginationParams } from '../../../shared/models/pagination.models';

export interface PayoutFilter {
  payeeIds: string[];
  planIds: string[];
  status: PayoutStatus | 'All';
  currencies: string[];
  periodFrom: string | null;
  periodTo: string | null;
  hideZero: boolean;
}

export const EMPTY_PAYOUT_FILTER: PayoutFilter = {
  payeeIds: [],
  planIds: [],
  status: 'All',
  currencies: [],
  periodFrom: null,
  periodTo: null,
  hideZero: true,
};

@Injectable({ providedIn: 'root' })
export class PayoutsStore {
  private readonly api = inject(PayoutsApiService);

  readonly loading = signal(false);
  readonly error = signal<string | null>(null);
  readonly page = signal(1);
  readonly pageSize = signal(25);
  readonly sortBy = signal('updatedAt');
  readonly sortOrder = signal<'asc' | 'desc'>('desc');
  readonly filter = signal<PayoutFilter>({ ...EMPTY_PAYOUT_FILTER });

  readonly pagedResult = signal<PagedResult<PayoutListItem> | null>(null);

  // Bulk selection
  readonly selectedIds = signal<Set<string>>(new Set());

  readonly items = computed(() => this.pagedResult()?.items ?? []);
  readonly totalCount = computed(() => this.pagedResult()?.totalCount ?? 0);
  readonly totalPages = computed(() => this.pagedResult()?.totalPages ?? 1);
  readonly hasNextPage = computed(() => this.pagedResult()?.hasNextPage ?? false);
  readonly hasPreviousPage = computed(() => this.pagedResult()?.hasPreviousPage ?? false);

  readonly selectedCount = computed(() => this.selectedIds().size);
  readonly allSelected = computed(() => {
    const items = this.items();
    const sel = this.selectedIds();
    return items.length > 0 && items.every(i => sel.has(i.id));
  });

  readonly selectedCalculatedIds = computed(() =>
    [...this.selectedIds()].filter(id =>
      this.items().find(i => i.id === id)?.status === 'Calculated'
    )
  );

  readonly activeFilterCount = computed(() => {
    const f = this.filter();
    let n = 0;
    if (f.payeeIds.length > 0) n++;
    if (f.planIds.length > 0) n++;
    if (f.status !== 'All') n++;
    if (f.currencies.length > 0) n++;
    if (f.periodFrom || f.periodTo) n++;
    return n;
  });

  readonly hasActiveFilters = computed(() => this.activeFilterCount() > 0);

  constructor() {
    effect(() => {
      void this._loadList(
        this.page(), this.pageSize(), this.sortBy(), this.sortOrder(), this.filter()
      );
    });
  }

  static _buildFilterRecord(f: PayoutFilter): Record<string, string> {
    const p: Record<string, string> = {};
    if (f.payeeIds.length > 0) p['payeeIds'] = f.payeeIds.join(',');
    if (f.planIds.length > 0) p['planIds'] = f.planIds.join(',');
    if (f.status !== 'All') p['status'] = f.status;
    if (f.currencies.length > 0) p['currencies'] = f.currencies.join(',');
    if (f.periodFrom) p['periodFrom'] = f.periodFrom;
    if (f.periodTo) p['periodTo'] = f.periodTo;
    if (f.hideZero) p['excludeZero'] = 'true';
    return p;
  }

  private async _loadList(
    page: number, pageSize: number, sortBy: string, sortOrder: 'asc' | 'desc', f: PayoutFilter
  ): Promise<void> {
    this.loading.set(true);
    this.error.set(null);
    try {
      const filters = PayoutsStore._buildFilterRecord(f);
      const params: PaginationParams = {
        page, pageSize, sortBy, sortOrder,
        filters: Object.keys(filters).length > 0 ? filters : undefined,
      };
      const data = await firstValueFrom(this.api.list(params));
      this.pagedResult.set(data);
      // Clear selection for rows no longer on screen
      const currentIds = new Set(data.items.map(i => i.id));
      this.selectedIds.update(sel => {
        const updated = new Set<string>();
        sel.forEach(id => { if (currentIds.has(id)) updated.add(id); });
        return updated;
      });
    } catch {
      this.error.set('ERRORS.GENERIC');
    } finally {
      this.loading.set(false);
    }
  }

  setFilter(partial: Partial<PayoutFilter>): void {
    this.filter.update(f => ({ ...f, ...partial }));
    this.page.set(1);
  }

  clearFilters(): void {
    this.filter.set({ ...EMPTY_PAYOUT_FILTER });
    this.page.set(1);
  }

  setPage(n: number): void { this.page.set(n); }
  setPageSize(n: number): void { this.pageSize.set(n); this.page.set(1); }

  // Selection
  toggleSelect(id: string): void {
    this.selectedIds.update(sel => {
      const next = new Set(sel);
      if (next.has(id)) next.delete(id); else next.add(id);
      return next;
    });
  }

  toggleSelectAll(): void {
    if (this.allSelected()) {
      this.selectedIds.set(new Set());
    } else {
      this.selectedIds.set(new Set(this.items().map(i => i.id)));
    }
  }

  clearSelection(): void { this.selectedIds.set(new Set()); }

  async reload(): Promise<void> {
    await this._loadList(
      this.page(), this.pageSize(), this.sortBy(), this.sortOrder(), this.filter()
    );
  }

  toQueryParams(): Record<string, string> {
    const f = this.filter();
    const p: Record<string, string> = {};
    if (f.payeeIds.length > 0) p['payeeIds'] = f.payeeIds.join(',');
    if (f.planIds.length > 0) p['planIds'] = f.planIds.join(',');
    if (f.status !== 'All') p['status'] = f.status;
    if (f.currencies.length > 0) p['currencies'] = f.currencies.join(',');
    if (f.periodFrom) p['pFrom'] = f.periodFrom;
    if (f.periodTo) p['pTo'] = f.periodTo;
    if (!f.hideZero) p['hz'] = '0';
    return p;
  }

  loadFromQueryParams(params: Record<string, string>): void {
    const f: PayoutFilter = { ...EMPTY_PAYOUT_FILTER };
    if (params['payeeIds']) f.payeeIds = params['payeeIds'].split(',').filter(Boolean);
    if (params['planIds']) f.planIds = params['planIds'].split(',').filter(Boolean);
    const st = params['status'] as PayoutStatus;
    if (st && ['Calculated', 'Approved', 'Paid', 'Disputed'].includes(st)) f.status = st;
    if (params['currencies']) f.currencies = params['currencies'].split(',').filter(Boolean);
    if (params['pFrom']) f.periodFrom = params['pFrom'];
    if (params['pTo']) f.periodTo = params['pTo'];
    if (params['hz'] === '0') f.hideZero = false;
    this.filter.set(f);
  }
}
