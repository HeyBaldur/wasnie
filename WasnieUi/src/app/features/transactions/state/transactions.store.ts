import { computed, effect, inject, Injectable, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { TransactionsApiService } from '../services/transactions.api.service';
import { Transaction, TransactionStatus, CreateTransactionRequest, AssignPayeeRequest, ReassignPayeeRequest } from '../models/transaction.model';
import { PagedResult, PaginationParams } from '../../../shared/models/pagination.models';

@Injectable({ providedIn: 'root' })
export class TransactionsStore {
  private readonly api = inject(TransactionsApiService);

  readonly loading = signal(false);
  readonly error = signal<string | null>(null);

  readonly page = signal(1);
  readonly pageSize = signal(10);
  readonly sortBy = signal('transactiondate');
  readonly sortOrder = signal<'asc' | 'desc'>('desc');
  readonly statusFilter = signal<TransactionStatus | null>(null);
  readonly payeeIdFilter = signal<string | null>(null);
  readonly dateFromFilter = signal<string | null>(null);
  readonly dateToFilter = signal<string | null>(null);

  readonly pagedResult = signal<PagedResult<Transaction> | null>(null);

  readonly transactions = computed(() => this.pagedResult()?.items ?? []);
  readonly totalCount = computed(() => this.pagedResult()?.totalCount ?? 0);
  readonly totalPages = computed(() => this.pagedResult()?.totalPages ?? 1);
  readonly hasNextPage = computed(() => this.pagedResult()?.hasNextPage ?? false);
  readonly hasPreviousPage = computed(() => this.pagedResult()?.hasPreviousPage ?? false);

  constructor() {
    effect(() => {
      const p = this.page();
      const ps = this.pageSize();
      const sb = this.sortBy();
      const so = this.sortOrder();
      const st = this.statusFilter();
      const pid = this.payeeIdFilter();
      const df = this.dateFromFilter();
      const dt = this.dateToFilter();
      void this._loadInternal(p, ps, sb, so, st, pid, df, dt);
    });
  }

  private async _loadInternal(
    page: number,
    pageSize: number,
    sortBy: string,
    sortOrder: 'asc' | 'desc',
    status: TransactionStatus | null,
    payeeId: string | null,
    dateFrom: string | null,
    dateTo: string | null,
  ): Promise<void> {
    this.loading.set(true);
    this.error.set(null);
    try {
      const filters: Record<string, string> = {};
      if (status !== null) filters['status'] = status;
      if (payeeId) filters['payeeId'] = payeeId;
      if (dateFrom) filters['dateFrom'] = dateFrom;
      if (dateTo) filters['dateTo'] = dateTo;

      const params: PaginationParams = {
        page,
        pageSize,
        sortBy,
        sortOrder,
        filters: Object.keys(filters).length > 0 ? filters : undefined,
      };
      const data = await firstValueFrom(this.api.list(params));
      this.pagedResult.set(data);
    } catch {
      this.error.set('ERRORS.GENERIC');
    } finally {
      this.loading.set(false);
    }
  }

  async loadTransactions(): Promise<void> {
    await this._loadInternal(
      this.page(), this.pageSize(), this.sortBy(), this.sortOrder(),
      this.statusFilter(), this.payeeIdFilter(), this.dateFromFilter(), this.dateToFilter()
    );
  }

  async createTransaction(request: CreateTransactionRequest): Promise<Transaction> {
    const tx = await firstValueFrom(this.api.create(request));
    await this.loadTransactions();
    return tx;
  }

  async assignPayee(transactionId: string, request: AssignPayeeRequest): Promise<Transaction> {
    const tx = await firstValueFrom(this.api.assignPayee(transactionId, request));
    await this.loadTransactions();
    return tx;
  }

  async reassignPayee(transactionId: string, request: ReassignPayeeRequest): Promise<Transaction> {
    const tx = await firstValueFrom(this.api.reassignPayee(transactionId, request));
    await this.loadTransactions();
    return tx;
  }

  setStatusFilter(value: TransactionStatus | null): void {
    this.statusFilter.set(value);
    this.page.set(1);
  }

  setPayeeIdFilter(value: string | null): void {
    this.payeeIdFilter.set(value);
    this.page.set(1);
  }

  setDateFromFilter(value: string | null): void {
    this.dateFromFilter.set(value);
    this.page.set(1);
  }

  setDateToFilter(value: string | null): void {
    this.dateToFilter.set(value);
    this.page.set(1);
  }

  setPage(value: number): void {
    this.page.set(value);
  }

  setPageSize(value: number): void {
    this.pageSize.set(value);
    this.page.set(1);
  }
}
