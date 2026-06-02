import { computed, effect, inject, Injectable, signal } from '@angular/core';
import { firstValueFrom, Subject } from 'rxjs';
import { debounceTime } from 'rxjs/operators';
import { toSignal } from '@angular/core/rxjs-interop';
import { PayeesApiService } from '../services/payees.api.service';
import { Payee, PayeeStatus, CreatePayeeRequest, UpdatePayeeRequest } from '../models/payee.model';
import { PagedResult, PaginationParams } from '../../../shared/models/pagination.models';

export interface PayeeListState {
  page: number;
  pageSize: number;
  search: string;
  sortBy: string;
  sortOrder: 'asc' | 'desc';
  status: PayeeStatus | null;
}

@Injectable({ providedIn: 'root' })
export class PayeesStore {
  private readonly api = inject(PayeesApiService);

  readonly selectedPayee = signal<Payee | null>(null);
  readonly loading = signal(false);
  readonly error = signal<string | null>(null);

  readonly page = signal(1);
  readonly pageSize = signal(10);
  readonly sortBy = signal('fullname');
  readonly sortOrder = signal<'asc' | 'desc'>('asc');
  readonly status = signal<PayeeStatus | null>(null);

  private readonly searchSubject$ = new Subject<string>();
  readonly search = toSignal(
    this.searchSubject$.pipe(debounceTime(300)),
    { initialValue: '' }
  );

  private _rawSearch = '';

  readonly pagedResult = signal<PagedResult<Payee> | null>(null);

  readonly payees = computed(() => this.pagedResult()?.items ?? []);
  readonly totalCount = computed(() => this.pagedResult()?.totalCount ?? 0);
  readonly totalPages = computed(() => this.pagedResult()?.totalPages ?? 1);
  readonly hasNextPage = computed(() => this.pagedResult()?.hasNextPage ?? false);
  readonly hasPreviousPage = computed(() => this.pagedResult()?.hasPreviousPage ?? false);

  // Expose listParams shape for template compat
  readonly listParams = computed<{ page: number; pageSize: number; search: string; status: PayeeStatus | null }>(() => ({
    page: this.page(),
    pageSize: this.pageSize(),
    search: this._rawSearch,
    status: this.status(),
  }));

  // pagedPayees = all items returned from server (already paged)
  readonly pagedPayees = computed(() => this.payees());

  constructor() {
    effect(() => {
      // Trigger re-load whenever any pagination param changes
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
    status: PayeeStatus | null,
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
        filters: status !== null ? { status: String(status) } : undefined,
      };
      const data = await firstValueFrom(this.api.getPayees(params));
      this.pagedResult.set(data);
    } catch {
      this.error.set('ERRORS.GENERIC');
    } finally {
      this.loading.set(false);
    }
  }

  async loadPayees(): Promise<void> {
    await this._loadInternal(
      this.page(), this.pageSize(), this.sortBy(), this.sortOrder(),
      this.status(), this.search()
    );
  }

  async loadPayee(payeeId: string): Promise<void> {
    this.loading.set(true);
    this.error.set(null);
    try {
      const data = await firstValueFrom(this.api.getPayee(payeeId));
      this.selectedPayee.set(data);
    } catch {
      this.error.set('ERRORS.GENERIC');
    } finally {
      this.loading.set(false);
    }
  }

  async createPayee(request: CreatePayeeRequest): Promise<Payee> {
    const payee = await firstValueFrom(this.api.createPayee(request));
    await this.loadPayees();
    return payee;
  }

  async updatePayee(payeeId: string, request: UpdatePayeeRequest): Promise<Payee> {
    const payee = await firstValueFrom(this.api.updatePayee(payeeId, request));
    this.selectedPayee.update((p) => (p?.id === payeeId ? payee : p));
    await this.loadPayees();
    return payee;
  }

  async markAsActive(payeeId: string): Promise<void> {
    await firstValueFrom(this.api.markAsActive(payeeId));
    await this.loadPayee(payeeId);
    await this.loadPayees();
  }

  async markAsOnLeave(payeeId: string): Promise<void> {
    await firstValueFrom(this.api.markAsOnLeave(payeeId));
    await this.loadPayee(payeeId);
    await this.loadPayees();
  }

  async markAsTerminated(payeeId: string, terminationDate: string): Promise<void> {
    await firstValueFrom(this.api.markAsTerminated(payeeId, terminationDate));
    await this.loadPayee(payeeId);
    await this.loadPayees();
  }

  async deactivate(payeeId: string): Promise<void> {
    await firstValueFrom(this.api.deactivate(payeeId));
    await this.loadPayee(payeeId);
    await this.loadPayees();
  }

  async activate(payeeId: string): Promise<void> {
    await firstValueFrom(this.api.activate(payeeId));
    await this.loadPayee(payeeId);
    await this.loadPayees();
  }

  setSearch(value: string): void {
    this._rawSearch = value;
    this.page.set(1);
    this.searchSubject$.next(value);
  }

  setStatus(value: PayeeStatus | null): void {
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

  // Legacy compat for components that use updateParams
  updateParams(partial: Partial<{ page: number; pageSize: number; search: string; status: PayeeStatus | null }>): void {
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
}
