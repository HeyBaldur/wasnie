import { TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { TransactionsStore } from './transactions.store';
import { TransactionsApiService } from '../services/transactions.api.service';
import { Transaction, TransactionStatus, TransactionSource } from '../models/transaction.model';
import { PagedResult } from '../../../shared/models/pagination.models';

const mockTransaction: Transaction = {
  id: 'tx-1',
  tenantId: 'tenant-1',
  referenceNumber: 'REF-001',
  payeeId: 'payee-1',
  amount: 500,
  currency: 'USD',
  transactionDate: '2024-01-15',
    ingestedAt: '2024-01-15T10:00:00Z',
  source: TransactionSource.Manual,
  status: TransactionStatus.Pending,
};

const mockPagedResult: PagedResult<Transaction> = {
  items: [mockTransaction],
  totalCount: 1,
  page: 1,
  pageSize: 10,
  totalPages: 1,
  hasNextPage: false,
  hasPreviousPage: false,
};

describe('TransactionsStore', () => {
  let store: TransactionsStore;
  let apiSpy: jasmine.SpyObj<TransactionsApiService>;

  beforeEach(() => {
    apiSpy = jasmine.createSpyObj('TransactionsApiService', ['list', 'create']);
    apiSpy.list.and.returnValue(of(mockPagedResult));
    apiSpy.create.and.returnValue(of(mockTransaction));

    TestBed.configureTestingModule({
      providers: [
        TransactionsStore,
        { provide: TransactionsApiService, useValue: apiSpy },
      ],
    });

    store = TestBed.inject(TransactionsStore);
  });

  it('loadTransactions() populates pagedResult', async () => {
    await store.loadTransactions();
    expect(store.transactions().length).toBe(1);
    expect(store.totalCount()).toBe(1);
  });

  it('setStatusFilter() resets page to 1 and updates filter', () => {
    store.setPage(3);
    store.setStatusFilter(TransactionStatus.Pending);
    expect(store.statusFilter()).toBe(TransactionStatus.Pending);
    expect(store.page()).toBe(1);
  });

  it('setStatusFilter(null) clears filter and resets page', () => {
    store.setStatusFilter(TransactionStatus.Paid);
    store.setPage(2);
    store.setStatusFilter(null);
    expect(store.statusFilter()).toBeNull();
    expect(store.page()).toBe(1);
  });

  it('setPageSize() resets page to 1', () => {
    store.setPage(5);
    store.setPageSize(25);
    expect(store.pageSize()).toBe(25);
    expect(store.page()).toBe(1);
  });

  it('createTransaction() calls api.create and reloads', async () => {
    await store.createTransaction({
      referenceNumber: 'REF-001',
      payeeId: 'payee-1',
      amount: 500,
      currency: 'USD',
      transactionDate: '2024-01-15',
    });

    expect(apiSpy.create).toHaveBeenCalledTimes(1);
    expect(apiSpy.list).toHaveBeenCalled();
  });

  it('loadTransactions() sets error signal on failure', async () => {
    apiSpy.list.and.returnValue(throwError(() => new Error('Network error')));
    await store.loadTransactions();
    expect(store.error()).toBe('ERRORS.GENERIC');
  });
});
