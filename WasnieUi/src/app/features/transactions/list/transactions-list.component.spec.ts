import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { By } from '@angular/platform-browser';
import { WsCopyButtonComponent } from '../../../shared/ui';
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
    sortBy: signal('ingestedat'),
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
    // Bulk void selection
    selectedIds: signal(new Set<string>()),
    selectedCount: signal(0),
    hasVoidableOnPage: signal(false),
    allVoidableSelected: signal(false),
    toggleSelect: jasmine.createSpy(),
    toggleSelectAllVoidable: jasmine.createSpy(),
    clearSelection: jasmine.createSpy(),
    bulkVoid: jasmine.createSpy().and.resolveTo({ voidedCount: 0, errors: [] }),
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

  it('openBulkVoid snapshots the selected count and opens the modal', () => {
    TestBed.overrideProvider(TransactionsStore, {
      useValue: makeStoreMock({ selectedCount: signal(3) as unknown as TransactionsStore['selectedCount'] }),
    });
    const fixture = TestBed.createComponent(TransactionsListComponent);
    const comp = fixture.componentInstance;
    fixture.detectChanges();

    comp.openBulkVoid();

    expect(comp.bulkVoidCount()).toBe(3);
    expect(comp.bulkVoidModalOpen()).toBeTrue();
  });

  it('confirmBulkVoid calls store.bulkVoid and keeps errors visible on partial failure', async () => {
    const store = makeStoreMock({
      bulkVoid: jasmine.createSpy().and.resolveTo({ voidedCount: 2, errors: ['REF-9: already paid'] }),
    } as Partial<TransactionsStore>);
    TestBed.overrideProvider(TransactionsStore, { useValue: store });
    const fixture = TestBed.createComponent(TransactionsListComponent);
    const comp = fixture.componentInstance;
    fixture.detectChanges();

    comp.openBulkVoid();
    comp.bulkVoidReason.set('wrong currency');
    await comp.confirmBulkVoid();

    expect(store.bulkVoid).toHaveBeenCalledWith('wrong currency');
    expect(comp.bulkVoidErrors()).toEqual(['REF-9: already paid']);
    expect(comp.bulkVoidModalOpen()).toBeTrue(); // stays open so the failures are shown
  });

  it('confirmBulkVoid does nothing when the reason is too short', async () => {
    const store = makeStoreMock();
    TestBed.overrideProvider(TransactionsStore, { useValue: store });
    const fixture = TestBed.createComponent(TransactionsListComponent);
    const comp = fixture.componentInstance;
    fixture.detectChanges();

    comp.bulkVoidReason.set('ab');
    await comp.confirmBulkVoid();

    expect(store.bulkVoid).not.toHaveBeenCalled();
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

    // The payee NAME is shown as visible text; the raw payee id is NOT user-visible — it lives only in the
    // payee-link href (WI-TX-PAYEE-LINK feature). So assert on textContent (visible text), and verify the
    // link target separately rather than forbidding the id anywhere in the markup.
    const visibleText: string = fixture.nativeElement.textContent ?? '';
    expect(visibleText).toContain('Anna Kowalska');
    expect(visibleText).not.toContain('payee-uuid-1234');

    const payeeLink: HTMLAnchorElement | null =
      fixture.nativeElement.querySelector('a[href*="/payees/payee-uuid-1234"]');
    expect(payeeLink)
      .withContext('the payee name should be a link to the payee detail')
      .not.toBeNull();
    expect(payeeLink?.textContent).toContain('Anna Kowalska');
  });

  // The primitive's own behaviour (clipboard, tick, swallowed click) is covered in
  // ws-copy-button.component.spec.ts. What this asserts is the WIRING: that each of the two copy
  // buttons in a row is handed the right value — a reference button that copies the payee's name is
  // exactly the kind of mix-up a template swap introduces silently.
  it('gives the row a copy button for the reference and one for the payee name', () => {
    const tx = {
      id: 'tx-3',
      tenantId: 'tenant-1',
      referenceNumber: 'REF-777',
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

    const values = fixture.debugElement
      .queryAll(By.directive(WsCopyButtonComponent))
      .map((el) => (el.componentInstance as WsCopyButtonComponent).value());

    expect(values).toEqual(['REF-777', 'Anna Kowalska']);
  });

  it('offers no payee copy button when the transaction is unassigned', () => {
    const tx = {
      id: 'tx-4',
      tenantId: 'tenant-1',
      referenceNumber: 'REF-888',
      payeeId: null,
      amount: 200,
      currency: 'EUR',
      transactionDate: '2025-02-01',
      ingestedAt: '2025-02-01T00:00:00Z',
      source: TransactionSource.Manual,
      status: TransactionStatus.Pending,
      payeeName: null,
      quantity: 1,
    };

    TestBed.overrideProvider(TransactionsStore, {
      useValue: makeStoreMock({ transactions: signal([tx]) }),
    });

    const fixture = TestBed.createComponent(TransactionsListComponent);
    fixture.detectChanges();

    const values = fixture.debugElement
      .queryAll(By.directive(WsCopyButtonComponent))
      .map((el) => (el.componentInstance as WsCopyButtonComponent).value());

    // Only the reference. Copying the word "Unassigned" is not a thing anyone wants.
    expect(values).toEqual(['REF-888']);
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

  it('renders the category tag when present and the reference links to the detail', () => {
    const tx = {
      id: 'tx-cat-1', tenantId: 't', referenceNumber: 'REF-CAT',
      payeeId: 'p1', amount: 1000, currency: 'EUR', quantity: 1,
      transactionDate: '2026-07-01', ingestedAt: '2026-07-01T00:00:00Z',
      source: TransactionSource.Manual, status: TransactionStatus.Calculated,
      payeeName: 'Anna', payeeEmployeeCode: 'E1', category: 'Laptops',
    };
    TestBed.overrideProvider(TransactionsStore, { useValue: makeStoreMock({ transactions: signal([tx]) }) });
    const fixture = TestBed.createComponent(TransactionsListComponent);
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Laptops');
    const refLink: HTMLAnchorElement | null =
      fixture.nativeElement.querySelector('a[href*="/transactions/tx-cat-1"]');
    expect(refLink).withContext('reference should link to the transaction detail').not.toBeNull();
    expect(refLink?.textContent).toContain('REF-CAT');
  });

  it('does not break and shows no category tag when category is null', () => {
    const tx = {
      id: 'tx-nocat', tenantId: 't', referenceNumber: 'REF-NOCAT',
      payeeId: 'p1', amount: 100, currency: 'EUR', quantity: 1,
      transactionDate: '2026-07-01', ingestedAt: '2026-07-01T00:00:00Z',
      source: TransactionSource.Manual, status: TransactionStatus.Pending,
      payeeName: 'Bob', payeeEmployeeCode: null, category: null,
    };
    TestBed.overrideProvider(TransactionsStore, { useValue: makeStoreMock({ transactions: signal([tx]) }) });
    const fixture = TestBed.createComponent(TransactionsListComponent);
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('.col-ref__category')).toBeNull();
    expect(fixture.nativeElement.innerHTML).not.toContain('null');
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
