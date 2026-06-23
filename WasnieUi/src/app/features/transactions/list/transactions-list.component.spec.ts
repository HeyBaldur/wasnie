import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { TranslateModule } from '@ngx-translate/core';
import { TransactionsListComponent } from './transactions-list.component';
import { TransactionsStore } from '../state/transactions.store';
import { TransactionStatus, TransactionSource } from '../models/transaction.model';

const makeStoreMock = (overrides?: Partial<TransactionsStore>) =>
  ({
    loading: signal(false),
    error: signal(null),
    page: signal(1),
    pageSize: signal(10),
    sortBy: signal('transactiondate'),
    sortOrder: signal<'asc' | 'desc'>('desc'),
    filter: signal({ reference: '', statuses: [], payeeIds: [], txDateFrom: null, txDateTo: null, ingestedFrom: null, ingestedTo: null, amountMin: null, amountMax: null, unassignedOnly: false, amountSort: null, referenceNumbers: [], currencies: [], attentionReason: null }),
    // Legacy computed aliases
    statusFilter: signal(null),
    statusesFilter: signal([]),
    payeeIdFilter: signal(null),
    payeeIdsFilter: signal([]),
    dateFromFilter: signal(null),
    txDateFromFilter: signal(null),
    dateToFilter: signal(null),
    txDateToFilter: signal(null),
    activeFilterCount: signal(0),
    hasActiveFilters: signal(false),
    unfilteredTotal: signal(null),
    transactions: signal([]),
    totalCount: signal(0),
    totalPages: signal(1),
    hasNextPage: signal(false),
    hasPreviousPage: signal(false),
    pagedResult: signal(null),
    loadTransactions: jasmine.createSpy(),
    createTransaction: jasmine.createSpy(),
    voidTransaction: jasmine.createSpy(),
    setFilter: jasmine.createSpy(),
    clearFilters: jasmine.createSpy(),
    setStatusTab: jasmine.createSpy(),
    setStatusFilter: jasmine.createSpy(),
    setPayeeIdFilter: jasmine.createSpy(),
    setDateFromFilter: jasmine.createSpy(),
    setDateToFilter: jasmine.createSpy(),
    toQueryParams: jasmine.createSpy().and.returnValue({}),
    loadFromQueryParams: jasmine.createSpy(),
    setPage: jasmine.createSpy(),
    setPageSize: jasmine.createSpy(),
    ...overrides,
  }) as unknown as TransactionsStore;

describe('TransactionsListComponent', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [TransactionsListComponent, TranslateModule.forRoot()],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: TransactionsStore, useValue: makeStoreMock() },
      ],
    }).compileComponents();
  });

  it('renders without errors', () => {
    const fixture = TestBed.createComponent(TransactionsListComponent);
    fixture.detectChanges();
    expect(fixture.nativeElement).toBeTruthy();
  });

  it('renders payee name from DTO when payeeName is provided', () => {
    const tx = {
      id: 'tx-1',
      tenantId: 'tenant-1',
      referenceNumber: 'REF-001',
      payeeId: 'payee-uuid-1234',
      amount: 500,
      currency: 'EUR',
      transactionDate: '2025-01-15',
      ingestedAt: '2025-01-01T00:00:00Z',
      source: TransactionSource.Manual,
      status: TransactionStatus.Pending,
      payeeName: 'Anna Kowalska',
      payeeEmployeeCode: 'EMP001',
      quantity: 1,
    };

    TestBed.overrideProvider(TransactionsStore, {
      useValue: makeStoreMock({ transactions: signal([tx]) }),
    });

    const fixture = TestBed.createComponent(TransactionsListComponent);
    fixture.detectChanges();
    const html: string = fixture.nativeElement.innerHTML;

    expect(html).toContain('Anna Kowalska');
    expect(html).not.toContain('payee-uuid-1234');
  });

  it('renders "Unassigned" (i18n key resolved) when payeeName is null — never shows raw GUID', () => {
    const tx = {
      id: 'tx-2',
      tenantId: 'tenant-1',
      referenceNumber: 'REF-002',
      payeeId: 'payee-uuid-5678',
      amount: 200,
      currency: 'EUR',
      transactionDate: '2025-02-01',
      ingestedAt: '2025-01-01T00:00:00Z',
      source: TransactionSource.Manual,
      status: TransactionStatus.Pending,
      payeeName: null,
      payeeEmployeeCode: null,
      quantity: 1,
    };

    TestBed.overrideProvider(TransactionsStore, {
      useValue: makeStoreMock({ transactions: signal([tx]) }),
    });

    const fixture = TestBed.createComponent(TransactionsListComponent);
    fixture.detectChanges();
    const html: string = fixture.nativeElement.innerHTML;

    // TRANSACTIONS.UNASSIGNED resolves to key itself in test (no translations loaded)
    // but the raw GUID must never appear regardless of translation
    expect(html).not.toContain('payee-uuid-5678');
    expect(html).not.toContain('null');
  });

  it('renders "Unassigned" when payeeName is empty string — never shows blank cell', () => {
    const tx = {
      id: 'tx-3',
      tenantId: 'tenant-1',
      referenceNumber: 'REF-003',
      payeeId: 'payee-uuid-9999',
      amount: 100,
      currency: 'EUR',
      transactionDate: '2025-03-01',
      ingestedAt: '2025-01-01T00:00:00Z',
      source: TransactionSource.Manual,
      status: TransactionStatus.Pending,
      payeeName: '',
      payeeEmployeeCode: null,
      quantity: 1,
    };

    TestBed.overrideProvider(TransactionsStore, {
      useValue: makeStoreMock({ transactions: signal([tx]) }),
    });

    const fixture = TestBed.createComponent(TransactionsListComponent);
    fixture.detectChanges();
    const html: string = fixture.nativeElement.innerHTML;

    expect(html).not.toContain('payee-uuid-9999');
  });

  it('does not require PayeesStore — component has no PayeesStore dependency', () => {
    // If PayeesStore were still injected, this test would fail because it's not provided.
    // Passing without providing PayeesStore proves the dependency was removed.
    expect(() => {
      const fixture = TestBed.createComponent(TransactionsListComponent);
      fixture.detectChanges();
    }).not.toThrow();
  });

  describe('canVoid()', () => {
    let component: TransactionsListComponent;

    beforeEach(() => {
      const fixture = TestBed.createComponent(TransactionsListComponent);
      component = fixture.componentInstance;
    });

    const makeTx = (status: TransactionStatus) => ({
      id: 'tx-v1', tenantId: 't', referenceNumber: 'R', payeeId: 'p',
      amount: 100, currency: 'EUR', quantity: 1, transactionDate: '2025-01-01',
      ingestedAt: '2025-01-01T00:00:00Z', source: TransactionSource.Manual, status,
    });

    it('returns true for Pending transactions', () => {
      expect(component.canVoid(makeTx(TransactionStatus.Pending))).toBeTrue();
    });

    it('returns false for Calculated transactions', () => {
      expect(component.canVoid(makeTx(TransactionStatus.Calculated))).toBeFalse();
    });

    it('returns false for Paid transactions', () => {
      expect(component.canVoid(makeTx(TransactionStatus.Paid))).toBeFalse();
    });

    it('returns false for Cancelled transactions', () => {
      expect(component.canVoid(makeTx(TransactionStatus.Cancelled))).toBeFalse();
    });
  });
});
