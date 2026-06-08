import { TestBed } from '@angular/core/testing';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';
import { TransactionsApiService } from './transactions.api.service';
import { Transaction, TransactionStatus, TransactionSource, CreateTransactionRequest } from '../models/transaction.model';
import { PagedResult } from '../../../shared/models/pagination.models';

describe('TransactionsApiService', () => {
  let service: TransactionsApiService;
  let httpMock: HttpTestingController;

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
    quantity: 1,
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

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(TransactionsApiService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('list() sends GET /api/transactions with pagination params', () => {
    service.list({ page: 1, pageSize: 10 }).subscribe((result) => {
      expect(result).toEqual(mockPagedResult);
    });

    const req = httpMock.expectOne((r) => r.url === '/api/transactions' && r.method === 'GET');
    expect(req.request.params.get('page')).toBe('1');
    expect(req.request.params.get('pageSize')).toBe('10');
    req.flush(mockPagedResult);
  });

  it('list() includes status filter as flat query param', () => {
    service.list({ page: 1, pageSize: 10, filters: { status: 'Pending' } }).subscribe();

    const req = httpMock.expectOne((r) => r.url === '/api/transactions');
    expect(req.request.params.get('status')).toBe('Pending');
    req.flush(mockPagedResult);
  });

  it('getById() sends GET /api/transactions/{id}', () => {
    service.getById('tx-1').subscribe((tx) => {
      expect(tx).toEqual(mockTransaction);
    });

    const req = httpMock.expectOne('/api/transactions/tx-1');
    expect(req.request.method).toBe('GET');
    req.flush(mockTransaction);
  });

  it('create() sends POST /api/transactions with body', () => {
    const request: CreateTransactionRequest = {
      referenceNumber: 'REF-001',
      payeeId: 'payee-1',
      amount: 500,
      currency: 'USD',
      transactionDate: '2024-01-15',
      quantity: 1,
    };

    service.create(request).subscribe((tx) => {
      expect(tx).toEqual(mockTransaction);
    });

    const req = httpMock.expectOne('/api/transactions');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual(request);
    req.flush(mockTransaction);
  });
});
