import { computed, effect, inject, Injectable, signal } from '@angular/core';
import { firstValueFrom, Subject } from 'rxjs';
import { debounceTime } from 'rxjs/operators';
import { toSignal } from '@angular/core/rxjs-interop';
import { QuotasApiService } from '../services/quotas.api.service';
import { QuotaSummary, QuotaStatus, QuotaListParams, CreateQuotaRequest } from '../models/quota.model';
import { PagedResult, PaginationParams } from '../../../shared/models/pagination.models';

@Injectable({ providedIn: 'root' })
export class QuotasStore {
  private readonly api = inject(QuotasApiService);

  readonly selectedQuota = signal<QuotaSummary | null>(null);
  readonly loading = signal(false);
  readonly error = signal<string | null>(null);

  readonly page = signal(1);
  readonly pageSize = signal(10);
  readonly sortBy = signal('periodstart');
  readonly sortOrder = signal<'asc' | 'desc'>('desc');
  readonly status = signal<QuotaStatus | null>(null);

  private readonly searchSubject$ = new Subject<string>();
  readonly search = toSignal(
    this.searchSubject$.pipe(debounceTime(300)),
    { initialValue: '' }
  );

  private _rawSearch = '';

  readonly pagedResult = signal<PagedResult<QuotaSummary> | null>(null);

  readonly quotas = computed(() => this.pagedResult()?.items ?? []);
  readonly totalCount = computed(() => this.pagedResult()?.totalCount ?? 0);
  readonly totalPages = computed(() => this.pagedResult()?.totalPages ?? 1);

  // Legacy compat
  readonly listParams = computed<QuotaListParams>(() => ({
    page: this.page(),
    pageSize: this.pageSize(),
    search: this._rawSearch,
    status: this.status(),
  }));

  readonly pagedQuotas = computed(() => this.quotas());
  readonly filteredQuotas = computed(() => this.quotas());

  constructor() {
    effect(() => {
      const p = this.page();
      const ps = this.pageSize();
      const sb = this.sortBy();
      const so = this.sortOrder();
      const st = this.status();
      const srch = this.search();
      void this._loadInternal(p, ps, sb, so, st, srch);
    });
  }

  private async _loadInternal(
    page: number,
    pageSize: number,
    sortBy: string,
    sortOrder: 'asc' | 'desc',
    status: QuotaStatus | null,
    search: string,
  ): Promise<void> {
    this.loading.set(true);
    this.error.set(null);
    try {
      const params: PaginationParams = {
        page,
        pageSize,
        sortBy,
        sortOrder,
        search: search || undefined,
        filters: status ? { status } : undefined,
      };
      const data = await firstValueFrom(this.api.getQuotas(params));
      this.pagedResult.set(data);
    } catch {
      this.error.set('ERRORS.GENERIC');
    } finally {
      this.loading.set(false);
    }
  }

  async loadQuotas(): Promise<void> {
    await this._loadInternal(
      this.page(), this.pageSize(), this.sortBy(), this.sortOrder(),
      this.status(), this.search()
    );
  }

  async loadQuota(quotaId: string): Promise<void> {
    this.loading.set(true);
    this.error.set(null);
    try {
      const data = await firstValueFrom(this.api.getQuota(quotaId));
      this.selectedQuota.set(data);
    } catch {
      this.error.set('ERRORS.GENERIC');
    } finally {
      this.loading.set(false);
    }
  }

  async createQuota(request: CreateQuotaRequest): Promise<QuotaSummary> {
    const quota = await firstValueFrom(this.api.createQuota(request));
    await this.loadQuotas();
    return quota;
  }

  async activateQuota(quotaId: string): Promise<void> {
    await firstValueFrom(this.api.activateQuota(quotaId));
    this._patchStatus(quotaId, 'Active');
  }

  async closeQuota(quotaId: string): Promise<void> {
    await firstValueFrom(this.api.closeQuota(quotaId));
    this._patchStatus(quotaId, 'Closed');
  }

  setSearch(value: string): void {
    this._rawSearch = value;
    this.page.set(1);
    this.searchSubject$.next(value);
  }

  setStatus(value: QuotaStatus | null): void {
    this.status.set(value);
    this.page.set(1);
  }

  setPage(value: number): void {
    this.page.set(value);
  }

  setPageSize(value: number): void {
    this.pageSize.set(value);
    this.page.set(1);
  }

  updateParams(partial: Partial<QuotaListParams>): void {
    if ('search' in partial && partial.search !== undefined) {
      this.setSearch(partial.search);
    }
    if ('status' in partial) {
      this.status.set(partial.status ?? null);
    }
    if ('pageSize' in partial && partial.pageSize !== undefined) {
      this.setPageSize(partial.pageSize);
    }
    if ('page' in partial && partial.page !== undefined) {
      this.page.set(partial.page);
    } else if ('search' in partial || 'status' in partial || 'pageSize' in partial) {
      this.page.set(1);
    }
  }

  private _patchStatus(quotaId: string, status: QuotaStatus): void {
    this.pagedResult.update((r) => r
      ? { ...r, items: r.items.map((q) => q.id === quotaId ? { ...q, status } : q) }
      : r);
    this.selectedQuota.update((q) =>
      q?.id === quotaId ? { ...q, status } : q
    );
  }
}
