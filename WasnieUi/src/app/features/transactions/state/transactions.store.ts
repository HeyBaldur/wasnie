import { computed, effect, inject, Injectable, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { TransactionsApiService } from '../services/transactions.api.service';
import {
  Transaction,
  TransactionStatus,
  CreateTransactionRequest,
  AssignPayeeRequest,
  ReassignPayeeRequest,
} from '../models/transaction.model';
import { PagedResult, PaginationParams } from '../../../shared/models/pagination.models';

export interface TransactionFilter {
  reference: string;
  statuses: TransactionStatus[];
  payeeIds: string[];
  txDateFrom: string | null;
  txDateTo: string | null;
  ingestedFrom: string | null;
  ingestedTo: string | null;
  amountMin: number | null;
  amountMax: number | null;
  unassignedOnly: boolean;
  amountSort: 'asc' | 'desc' | null;
  referenceNumbers: string[];
}

export const EMPTY_FILTER: TransactionFilter = {
  reference: '',
  statuses: [],
  payeeIds: [],
  txDateFrom: null,
  txDateTo: null,
  ingestedFrom: null,
  ingestedTo: null,
  amountMin: null,
  amountMax: null,
  unassignedOnly: false,
  amountSort: null,
  referenceNumbers: [],
};

@Injectable({ providedIn: 'root' })
export class TransactionsStore {
  private readonly api = inject(TransactionsApiService);

  readonly loading = signal(false);
  readonly error = signal<string | null>(null);

  readonly page = signal(1);
  readonly pageSize = signal(10);
  readonly sortBy = signal('transactiondate');
  readonly sortOrder = signal<'asc' | 'desc'>('desc');

  // Filter signals
  readonly filter = signal<TransactionFilter>({ ...EMPTY_FILTER });

  // Convenience computed accessors
  readonly statusesFilter = computed(() => this.filter().statuses);
  readonly payeeIdsFilter = computed(() => this.filter().payeeIds);
  readonly txDateFromFilter = computed(() => this.filter().txDateFrom);
  readonly txDateToFilter = computed(() => this.filter().txDateTo);

  // Legacy aliases — kept for ProcessPendingComponent compat
  readonly payeeIdFilter = computed(() => this.filter().payeeIds[0] ?? null);
  readonly dateFromFilter = computed(() => this.filter().txDateFrom);
  readonly dateToFilter = computed(() => this.filter().txDateTo);
  readonly statusFilter = computed(() =>
    this.filter().statuses.length === 1 ? this.filter().statuses[0] : null
  );

  readonly pagedResult = signal<PagedResult<Transaction> | null>(null);

  readonly transactions = computed(() => this.pagedResult()?.items ?? []);
  readonly totalCount = computed(() => this.pagedResult()?.totalCount ?? 0);
  readonly unfilteredTotal = computed(() => this.pagedResult()?.unfilteredTotal ?? null);
  readonly totalPages = computed(() => this.pagedResult()?.totalPages ?? 1);
  readonly hasNextPage = computed(() => this.pagedResult()?.hasNextPage ?? false);
  readonly hasPreviousPage = computed(() => this.pagedResult()?.hasPreviousPage ?? false);

  readonly activeFilterCount = computed(() => {
    const f = this.filter();
    let count = 0;
    if (f.reference) count++;
    if (f.statuses.length > 0) count++;
    if (f.payeeIds.length > 0) count++;
    if (f.txDateFrom || f.txDateTo) count++;
    if (f.ingestedFrom || f.ingestedTo) count++;
    if (f.amountMin !== null || f.amountMax !== null) count++;
    if (f.unassignedOnly) count++;
    if (f.amountSort) count++;
    if (f.referenceNumbers.length > 0) count++;
    return count;
  });

  readonly hasActiveFilters = computed(() => this.activeFilterCount() > 0);

  constructor() {
    effect(() => {
      const p = this.page();
      const ps = this.pageSize();
      const sb = this.sortBy();
      const so = this.sortOrder();
      const f = this.filter();
      void this._loadInternal(p, ps, sb, so, f);
    });
  }

  // Single source of truth for TransactionFilter → API field names.
  // Used by both _loadInternal (list) and toExportFilter (export) to guarantee identical predicates.
  private static _buildFilterRecord(f: TransactionFilter): Record<string, string> {
    const filters: Record<string, string> = {};
    if (f.reference) filters['reference'] = f.reference;
    if (f.statuses.length > 0) filters['statuses'] = f.statuses.join(',');
    if (f.payeeIds.length > 0) filters['payeeIds'] = f.payeeIds.join(',');
    if (f.txDateFrom) filters['dateFrom'] = f.txDateFrom;
    if (f.txDateTo) filters['dateTo'] = f.txDateTo;
    if (f.ingestedFrom) filters['ingestedFrom'] = f.ingestedFrom;
    if (f.ingestedTo) filters['ingestedTo'] = f.ingestedTo;
    if (f.amountMin !== null) filters['amountMin'] = String(f.amountMin);
    if (f.amountMax !== null) filters['amountMax'] = String(f.amountMax);
    if (f.unassignedOnly) filters['unassignedOnly'] = 'true';
    if (f.amountSort) filters['amountSort'] = f.amountSort;
    if (f.referenceNumbers.length > 0) filters['referenceNumbers'] = f.referenceNumbers.join(',');
    return filters;
  }

  // Returns the filter as a typed JSON object for POST body to export endpoint.
  // PaginationQuery uses [FromBody] JSON deserialization — bool? and decimal? must be actual
  // JSON booleans/numbers, not strings. _buildFilterRecord produces strings (correct for GET
  // query params where model binding is lenient), but JSON body deserialization is strict.
  toExportFilter(): Record<string, unknown> {
    const f = this.filter();
    const body: Record<string, unknown> = {};
    if (f.reference) body['reference'] = f.reference;
    if (f.statuses.length > 0) body['statuses'] = f.statuses.join(',');
    if (f.payeeIds.length > 0) body['payeeIds'] = f.payeeIds.join(',');
    if (f.txDateFrom) body['dateFrom'] = f.txDateFrom;
    if (f.txDateTo) body['dateTo'] = f.txDateTo;
    if (f.ingestedFrom) body['ingestedFrom'] = f.ingestedFrom;
    if (f.ingestedTo) body['ingestedTo'] = f.ingestedTo;
    if (f.amountMin !== null) body['amountMin'] = f.amountMin;      // decimal? — must be number
    if (f.amountMax !== null) body['amountMax'] = f.amountMax;      // decimal? — must be number
    if (f.unassignedOnly) body['unassignedOnly'] = true;            // bool? — must be boolean
    if (f.amountSort) body['amountSort'] = f.amountSort;
    if (f.referenceNumbers.length > 0) body['referenceNumbers'] = f.referenceNumbers.join(',');
    return body;
  }

  private async _loadInternal(
    page: number,
    pageSize: number,
    sortBy: string,
    sortOrder: 'asc' | 'desc',
    f: TransactionFilter,
  ): Promise<void> {
    this.loading.set(true);
    this.error.set(null);
    try {
      const filters = TransactionsStore._buildFilterRecord(f);

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
      this.page(), this.pageSize(), this.sortBy(), this.sortOrder(), this.filter()
    );
  }

  setFilter(partial: Partial<TransactionFilter>): void {
    this.filter.update(f => ({ ...f, ...partial }));
    this.page.set(1);
  }

  clearFilters(): void {
    this.filter.set({ ...EMPTY_FILTER });
    this.page.set(1);
  }

  /** Set a single status (tab shortcut — replaces any current status filter). */
  setStatusTab(status: TransactionStatus | null): void {
    this.filter.update(f => ({ ...f, statuses: status ? [status] : [] }));
    this.page.set(1);
  }

  // Legacy setters — kept for backward compat with ProcessPendingComponent
  setStatusFilter(value: TransactionStatus | null): void {
    this.setStatusTab(value);
  }

  setPayeeIdFilter(value: string | null): void {
    this.filter.update(f => ({ ...f, payeeIds: value ? [value] : [] }));
    this.page.set(1);
  }

  setDateFromFilter(value: string | null): void {
    this.filter.update(f => ({ ...f, txDateFrom: value }));
    this.page.set(1);
  }

  setDateToFilter(value: string | null): void {
    this.filter.update(f => ({ ...f, txDateTo: value }));
    this.page.set(1);
  }

  setPage(value: number): void {
    this.page.set(value);
  }

  setPageSize(value: number): void {
    this.pageSize.set(value);
    this.page.set(1);
  }

  // ── URL query param serialization ──────────────────────────────────────────

  toQueryParams(): Record<string, string> {
    const f = this.filter();
    const params: Record<string, string> = {};
    if (f.reference) params['ref'] = f.reference;
    if (f.statuses.length > 0) params['statuses'] = f.statuses.join(',');
    if (f.payeeIds.length > 0) params['payeeIds'] = f.payeeIds.join(',');
    if (f.txDateFrom) params['txFrom'] = f.txDateFrom;
    if (f.txDateTo) params['txTo'] = f.txDateTo;
    if (f.ingestedFrom) params['ingFrom'] = f.ingestedFrom;
    if (f.ingestedTo) params['ingTo'] = f.ingestedTo;
    if (f.amountMin !== null) params['amtMin'] = String(f.amountMin);
    if (f.amountMax !== null) params['amtMax'] = String(f.amountMax);
    if (f.unassignedOnly) params['unassigned'] = '1';
    if (f.amountSort) params['amtSort'] = f.amountSort;
    if (f.referenceNumbers.length > 0) params['refs'] = f.referenceNumbers.join(',');
    return params;
  }

  loadFromQueryParams(params: Record<string, string>): void {
    const f: TransactionFilter = { ...EMPTY_FILTER };
    if (params['ref']) f.reference = params['ref'];
    if (params['statuses']) {
      f.statuses = params['statuses'].split(',')
        .filter(s => Object.values(TransactionStatus).includes(s as TransactionStatus))
        .map(s => s as TransactionStatus);
    }
    if (params['payeeIds']) f.payeeIds = params['payeeIds'].split(',').filter(Boolean);
    if (params['txFrom']) f.txDateFrom = params['txFrom'];
    if (params['txTo']) f.txDateTo = params['txTo'];
    if (params['ingFrom']) f.ingestedFrom = params['ingFrom'];
    if (params['ingTo']) f.ingestedTo = params['ingTo'];
    if (params['amtMin']) f.amountMin = Number(params['amtMin']) || null;
    if (params['amtMax']) f.amountMax = Number(params['amtMax']) || null;
    if (params['unassigned'] === '1') f.unassignedOnly = true;
    if (params['amtSort'] === 'asc' || params['amtSort'] === 'desc') f.amountSort = params['amtSort'];
    if (params['refs']) f.referenceNumbers = params['refs'].split(',').filter(Boolean);
    this.filter.set(f);
  }

  // ── Mutations ─────────────────────────────────────────────────────────────

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
}
