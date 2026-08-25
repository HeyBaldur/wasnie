import { computed, effect, inject, Injectable, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { CreditsApiService } from '../services/credits.api.service';
import { CreditListItem, CreditCounters, CreditByPayee } from '../models/credit.model';
import { PagedResult, PaginationParams } from '../../../shared/models/pagination.models';
import { LatestRequestGuard } from '../../../shared/state/latest-request-guard';

export type CreditStatus = 'Active' | 'Superseded' | 'All';

export interface CreditFilter {
  payeeIds: string[];
  planIds: string[];
  status: CreditStatus;
  allocatedFrom: string | null;
  allocatedTo: string | null;
  amountMin: number | null;
  amountMax: number | null;
  currencies: string[];
  reference: string;
  ruleIds: string[];
}

export const EMPTY_CREDIT_FILTER: CreditFilter = {
  payeeIds: [],
  planIds: [],
  status: 'Active',
  allocatedFrom: null,
  allocatedTo: null,
  amountMin: null,
  amountMax: null,
  currencies: [],
  reference: '',
  ruleIds: [],
};

@Injectable({ providedIn: 'root' })
export class CreditsStore {
  private readonly api = inject(CreditsApiService);

  readonly loading = signal(false);
  readonly error = signal<string | null>(null);
  readonly page = signal(1);
  readonly pageSize = signal(10);
  readonly sortBy = signal('allocatedAt');
  readonly sortOrder = signal<'asc' | 'desc'>('desc');
  readonly filter = signal<CreditFilter>({ ...EMPTY_CREDIT_FILTER });

  readonly pagedResult = signal<PagedResult<CreditListItem> | null>(null);
  readonly counters = signal<CreditCounters | null>(null);
  readonly byPayee = signal<CreditByPayee[]>([]);
  readonly byPayeeLoading = signal(false);

  readonly items = computed(() => this.pagedResult()?.items ?? []);
  readonly totalCount = computed(() => this.pagedResult()?.totalCount ?? 0);
  readonly unfilteredTotal = computed(() => this.pagedResult()?.unfilteredTotal ?? null);
  readonly totalPages = computed(() => this.pagedResult()?.totalPages ?? 1);
  readonly hasNextPage = computed(() => this.pagedResult()?.hasNextPage ?? false);
  readonly hasPreviousPage = computed(() => this.pagedResult()?.hasPreviousPage ?? false);

  readonly activeFilterCount = computed(() => {
    const f = this.filter();
    let n = 0;
    if (f.payeeIds.length > 0) n++;
    if (f.planIds.length > 0) n++;
    if (f.status !== 'Active') n++;
    if (f.allocatedFrom || f.allocatedTo) n++;
    if (f.amountMin !== null || f.amountMax !== null) n++;
    if (f.currencies.length > 0) n++;
    if (f.reference) n++;
    if (f.ruleIds.length > 0) n++;
    return n;
  });

  readonly hasActiveFilters = computed(() => this.activeFilterCount() > 0);

  // Makes the last-REQUESTED load win rather than the last-ARRIVED one. Change the filter while a
  // fetch is still in flight and two requests race; without this the slower, wider query can land
  // last and overwrite the narrower one, leaving the list looking unfiltered until a manual reload.
  private readonly _latest = new LatestRequestGuard();

  constructor() {
    effect(() => {
      void this._loadList(
        this.page(), this.pageSize(), this.sortBy(), this.sortOrder(), this.filter()
      );
    });
  }

  // Single source of truth: CreditFilter → API query param names.
  // FIX-11 rule: EVERY field must be here — missing a field = silent filter gap.
  static _buildFilterRecord(f: CreditFilter): Record<string, string> {
    const p: Record<string, string> = {};
    if (f.payeeIds.length > 0) p['payeeIds'] = f.payeeIds.join(',');
    if (f.planIds.length > 0) p['planIds'] = f.planIds.join(',');
    p['status'] = f.status;
    if (f.allocatedFrom) p['allocatedFrom'] = f.allocatedFrom;
    if (f.allocatedTo) p['allocatedTo'] = f.allocatedTo;
    if (f.amountMin !== null) p['amountMin'] = String(f.amountMin);
    if (f.amountMax !== null) p['amountMax'] = String(f.amountMax);
    if (f.currencies.length > 0) p['currencies'] = f.currencies.join(',');
    if (f.reference) p['reference'] = f.reference;
    if (f.ruleIds.length > 0) p['ruleIds'] = f.ruleIds.join(',');
    return p;
  }

  private async _loadList(
    page: number, pageSize: number, sortBy: string, sortOrder: 'asc' | 'desc', f: CreditFilter
  ): Promise<void> {
    const token = this._latest.begin();
    this.loading.set(true);
    this.error.set(null);
    try {
      const filters = CreditsStore._buildFilterRecord(f);
      const params: PaginationParams = {
        page, pageSize, sortBy, sortOrder,
        filters: Object.keys(filters).length > 0 ? filters : undefined,
      };
      const data = await firstValueFrom(this.api.list(params));
      if (this._latest.isStale(token)) return;   // superseded by a newer load — discard
      this.pagedResult.set(data);
    } catch {
      if (this._latest.isStale(token)) return;   // don't let a stale failure clobber a fresh result
      this.error.set('ERRORS.GENERIC');
    } finally {
      // Only the newest request owns the spinner; a stale one finishing must not clear it.
      if (!this._latest.isStale(token)) this.loading.set(false);
    }
  }

  async loadCounters(): Promise<void> {
    try {
      const filters = CreditsStore._buildFilterRecord(this.filter());
      const params: PaginationParams = {
        page: 1, pageSize: 1,
        filters: Object.keys(filters).length > 0 ? filters : undefined,
      };
      const data = await firstValueFrom(this.api.counters(params));
      this.counters.set(data);
    } catch { /* non-critical */ }
  }

  async loadByPayee(): Promise<void> {
    this.byPayeeLoading.set(true);
    try {
      const filters = CreditsStore._buildFilterRecord(this.filter());
      const params: PaginationParams = {
        page: 1, pageSize: 1,
        filters: Object.keys(filters).length > 0 ? filters : undefined,
      };
      const data = await firstValueFrom(this.api.byPayee(params));
      this.byPayee.set(data);
    } catch { /* non-critical */ } finally {
      this.byPayeeLoading.set(false);
    }
  }

  async loadAll(): Promise<void> {
    await Promise.all([this.loadCounters(), this.loadByPayee()]);
  }

  /** RefreshableStore — reload list + counters on route re-entry. */
  refresh(): Promise<void> {
    return this.loadAll();
  }

  setFilter(partial: Partial<CreditFilter>): void {
    this.filter.update(f => ({ ...f, ...partial }));
    this.page.set(1);
  }

  clearFilters(): void {
    this.filter.set({ ...EMPTY_CREDIT_FILTER });
    this.page.set(1);
  }

  setPage(n: number): void { this.page.set(n); }
  setPageSize(n: number): void { this.pageSize.set(n); this.page.set(1); }

  // Returns params for the GET /api/credits/export endpoint (same shape as list params).
  toExportParams(): PaginationParams {
    const filters = CreditsStore._buildFilterRecord(this.filter());
    return {
      page: 1,
      pageSize: 1,
      filters: Object.keys(filters).length > 0 ? filters : undefined,
    };
  }

  // URL sync
  toQueryParams(): Record<string, string> {
    const f = this.filter();
    const p: Record<string, string> = {};
    if (f.payeeIds.length > 0) p['payeeIds'] = f.payeeIds.join(',');
    if (f.planIds.length > 0) p['planIds'] = f.planIds.join(',');
    if (f.status !== 'Active') p['status'] = f.status;
    if (f.allocatedFrom) p['allocFrom'] = f.allocatedFrom;
    if (f.allocatedTo) p['allocTo'] = f.allocatedTo;
    if (f.amountMin !== null) p['amtMin'] = String(f.amountMin);
    if (f.amountMax !== null) p['amtMax'] = String(f.amountMax);
    if (f.currencies.length > 0) p['currencies'] = f.currencies.join(',');
    if (f.reference) p['ref'] = f.reference;
    if (f.ruleIds.length > 0) p['ruleIds'] = f.ruleIds.join(',');
    return p;
  }

  loadFromQueryParams(params: Record<string, string>): void {
    const f: CreditFilter = { ...EMPTY_CREDIT_FILTER };
    if (params['payeeIds']) f.payeeIds = params['payeeIds'].split(',').filter(Boolean);
    if (params['planIds']) f.planIds = params['planIds'].split(',').filter(Boolean);
    if (params['status'] === 'Superseded' || params['status'] === 'All') f.status = params['status'];
    if (params['allocFrom']) f.allocatedFrom = params['allocFrom'];
    if (params['allocTo']) f.allocatedTo = params['allocTo'];
    if (params['amtMin']) f.amountMin = Number(params['amtMin']) || null;
    if (params['amtMax']) f.amountMax = Number(params['amtMax']) || null;
    if (params['currencies']) f.currencies = params['currencies'].split(',').filter(Boolean);
    if (params['ref']) f.reference = params['ref'];
    if (params['ruleIds']) f.ruleIds = params['ruleIds'].split(',').filter(Boolean);
    this.filter.set(f);
  }
}
