import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { ActivatedRoute, Router } from '@angular/router';
import { BehaviorSubject, Observable, of, throwError } from 'rxjs';

import { PayoutsListComponent } from './payouts-list.component';
import { PayoutsApiService } from '../services/payouts.api.service';
import { PayoutsStore, EMPTY_PAYOUT_FILTER } from '../state/payouts.store';
import { PayeesApiService } from '../../payees/services/payees.api.service';
import { PlansApiService } from '../../plans/services/plans.api.service';
import { PagedResult } from '../../../shared/models/pagination.models';
import { PayoutListItem, BulkMarkPaidResult, PayoutStatus } from '../models/payout.model';

const EMPTY_PAGE: PagedResult<PayoutListItem> = {
  items: [],
  totalCount: 0,
  page: 1,
  pageSize: 25,
  totalPages: 1,
  hasNextPage: false,
  hasPreviousPage: false,
  unfilteredTotal: undefined,
};

const EMPTY_BULK_APPROVE_SUMMARY = { count: 0, payees: [] as { payeeId: string; payeeName: string; payeeCode: string; periodStart: string; periodEnd: string; amount: number; currency: string }[], skippedCount: 0 };
const EMPTY_BULK_MARK_PAID_SUMMARY = { count: 0, payees: [] as { payeeId: string; payeeName: string; payeeCode: string; periodStart: string; periodEnd: string; amount: number; currency: string }[], totalsByCurrency: new Map<string, number>(), skippedCount: 0 };

function makeStoreMock(): jasmine.SpyObj<PayoutsStore> {
  const m = jasmine.createSpyObj<PayoutsStore>('PayoutsStore', [
    'setFilter', 'clearFilters', 'setPage', 'setPageSize',
    'loadFromQueryParams', 'toQueryParams', 'toExportParams', 'clearSelection',
    'toggleSelect', 'toggleSelectAll', 'reload',
    // signal-like accessors (called as functions in the component)
    'filter', 'items', 'loading', 'error', 'selectedIds',
    'selectedCalculatedIds', 'selectedApprovedIds',
    'bulkApproveSummary', 'bulkMarkPaidSummary',
    'hasActiveFilters', 'activeFilterCount',
    'selectedCount', 'allSelected', 'page', 'pageSize', 'totalCount', 'totalPages',
  ]);
  m.filter.and.returnValue({ ...EMPTY_PAYOUT_FILTER });
  m.items.and.returnValue([]);
  m.loading.and.returnValue(false);
  m.error.and.returnValue(null);
  m.selectedIds.and.returnValue(new Set<string>());
  m.selectedCalculatedIds.and.returnValue([]);
  m.selectedApprovedIds.and.returnValue([]);
  m.bulkApproveSummary.and.returnValue(EMPTY_BULK_APPROVE_SUMMARY);
  m.bulkMarkPaidSummary.and.returnValue(EMPTY_BULK_MARK_PAID_SUMMARY);
  m.hasActiveFilters.and.returnValue(false);
  m.activeFilterCount.and.returnValue(0);
  m.selectedCount.and.returnValue(0);
  m.allSelected.and.returnValue(false);
  m.page.and.returnValue(1);
  m.pageSize.and.returnValue(25);
  m.totalCount.and.returnValue(0);
  m.totalPages.and.returnValue(1);
  m.toQueryParams.and.returnValue({});
  m.toExportParams.and.returnValue({ excludeZero: 'true' });
  m.reload.and.returnValue(Promise.resolve());
  return m;
}

// ─── Bulk Mark as Paid ───────────────────────────────────────────────────────
describe('PayoutsListComponent — onBulkMarkPaid', () => {
  let component: PayoutsListComponent;
  let apiSpy: jasmine.SpyObj<PayoutsApiService>;
  let storeMock: jasmine.SpyObj<PayoutsStore>;

  beforeEach(() => {
    apiSpy = jasmine.createSpyObj<PayoutsApiService>('PayoutsApiService', [
      'list', 'calculate', 'getJobStatus', 'bulkApprove', 'bulkMarkPaid', 'approve', 'markPaid', 'exportPdf', 'exportToExcel',
    ]);
    apiSpy.list.and.returnValue(of(EMPTY_PAGE));

    storeMock = makeStoreMock();

    TestBed.configureTestingModule({
      imports: [PayoutsListComponent],
      providers: [
        // The component asks CurrentUserService whether the ids in the bulk-refusal banner may be
        // links, and that service injects HttpClient. Nothing here talks to the network; the
        // testing backend just makes the graph constructible.
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: PayoutsApiService, useValue: apiSpy },
        { provide: PayoutsStore,      useValue: storeMock },
        { provide: PayeesApiService,  useValue: jasmine.createSpyObj('PayeesApiService', ['getPayees']) },
        { provide: PlansApiService,   useValue: jasmine.createSpyObj('PlansApiService', ['getPlans', 'getPlan']) },
        { provide: ActivatedRoute,    useValue: { snapshot: { queryParams: {} }, queryParams: of({}) } },
        { provide: Router,            useValue: jasmine.createSpyObj('Router', ['navigate']) },
      ],
    });

    TestBed.overrideComponent(PayoutsListComponent, {
      set: { template: '<div></div>', imports: [] },
    });

    const fixture = TestBed.createComponent(PayoutsListComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
    storeMock.reload.calls.reset(); // ngOnInit calls reload() once on activation; reset so bulk-action tests measure only action-triggered calls
  });

  it('calls bulkMarkPaid with approved IDs, clears selection, and reloads once', async () => {
    storeMock.selectedApprovedIds.and.returnValue(['id-1', 'id-2']);
    const mockResult: BulkMarkPaidResult = { paid: 2, errors: [] };
    apiSpy.bulkMarkPaid.and.returnValue(of(mockResult));

    await component.onBulkMarkPaid();

    expect(apiSpy.bulkMarkPaid).toHaveBeenCalledOnceWith({ payoutIds: ['id-1', 'id-2'] });
    expect(storeMock.clearSelection).toHaveBeenCalledTimes(1);
    expect(storeMock.reload).toHaveBeenCalledTimes(1);
  });

  it('is a no-op when there are no approved IDs', async () => {
    storeMock.selectedApprovedIds.and.returnValue([]);

    await component.onBulkMarkPaid();

    expect(apiSpy.bulkMarkPaid).not.toHaveBeenCalled();
    expect(storeMock.reload).not.toHaveBeenCalled();
  });

  it('does not reload if the API call throws', async () => {
    storeMock.selectedApprovedIds.and.returnValue(['id-1']);
    apiSpy.bulkMarkPaid.and.returnValue(throwError(() => new Error('network error')) as Observable<BulkMarkPaidResult>);

    try {
      await component.onBulkMarkPaid();
    } catch { /* expected */ }

    expect(storeMock.reload).not.toHaveBeenCalled();
    expect(component.bulkMarkPaiding()).toBe(false);
  });
});

// ─── PayoutsStore — bulk summary computeds ───────────────────────────────────
//
// Verifies that bulkApproveSummary and bulkMarkPaidSummary expose
// { payeeId, payeeName, payeeCode } for each selected item so that
// the confirmation modals can render clickable payee links.
// ─────────────────────────────────────────────────────────────────────────────

function makePayoutListItem(
  id: string,
  status: PayoutStatus,
  payeeId: string,
  payeeName: string,
  payeeCode: string,
  amount = 100,
  currency = 'EUR',
): PayoutListItem {
  return {
    id, payeeId, payeeName, payeeCode,
    planId: 'plan-1', planName: 'Test Plan',
    periodStart: '2026-01-01', periodEnd: '2026-03-31',
    totalCommissionAmount: amount, totalCommissionCurrency: currency,
    status,
    calculatedAt: '2026-06-01T00:00:00Z', calculatedBy: 'test',
    updatedAt: '2026-06-01T00:00:00Z', updatedBy: 'test',
    paidAt: null,
  };
}

describe('PayoutsStore — bulkApproveSummary', () => {
  let store: PayoutsStore;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        // The component asks CurrentUserService whether the ids in the bulk-refusal banner may be
        // links, and that service injects HttpClient. Nothing here talks to the network; the
        // testing backend just makes the graph constructible.
        provideHttpClient(),
        provideHttpClientTesting(),
        PayoutsStore,
        { provide: PayoutsApiService, useValue: jasmine.createSpyObj('PayoutsApiService', { list: of(EMPTY_PAGE) }) },
      ],
    });
    store = TestBed.inject(PayoutsStore);
  });

  it('exposes { payeeId, payeeName, payeeCode } for each Calculated item in selection', () => {
    const items = [
      makePayoutListItem('p1', 'Calculated', 'payee-1', 'Alice Smith', 'EMP-001'),
      makePayoutListItem('p2', 'Calculated', 'payee-2', 'Bob Jones', 'EMP-002'),
      makePayoutListItem('p3', 'Approved',   'payee-3', 'Carol Wu', 'EMP-003'),
    ];
    store['pagedResult'].set({ items, totalCount: 3, page: 1, pageSize: 25, totalPages: 1, hasNextPage: false, hasPreviousPage: false, unfilteredTotal: undefined });
    store.selectedIds.set(new Set(['p1', 'p2', 'p3']));

    const summary = store.bulkApproveSummary();

    expect(summary.count).toBe(2); // only Calculated
    expect(summary.skippedCount).toBe(1); // Approved is skipped
    expect(summary.payees).toEqual([
      { payeeId: 'payee-1', payeeName: 'Alice Smith', payeeCode: 'EMP-001', periodStart: '2026-01-01', periodEnd: '2026-03-31', amount: 100, currency: 'EUR' },
      { payeeId: 'payee-2', payeeName: 'Bob Jones',   payeeCode: 'EMP-002', periodStart: '2026-01-01', periodEnd: '2026-03-31', amount: 100, currency: 'EUR' },
    ]);
  });
});

// ─── Route activation: stale-data prevention ─────────────────────────────────
//
// PayoutsStore is providedIn: 'root' (singleton). Its effect() only fires when a
// signal changes. On re-navigation with the same filter the effect is silent.
// Refresh-on-entry is now handled by the shared [refreshOnEnter] directive (RouteRefreshTracker),
// NOT by an ad-hoc store.reload() in ngOnInit. This test guards that the ad-hoc call was removed.
// ─────────────────────────────────────────────────────────────────────────────
describe('PayoutsListComponent — ngOnInit reload', () => {
  let storeMock: jasmine.SpyObj<PayoutsStore>;
  let apiSpy: jasmine.SpyObj<PayoutsApiService>;

  beforeEach(() => {
    apiSpy = jasmine.createSpyObj<PayoutsApiService>('PayoutsApiService', [
      'list', 'calculate', 'getJobStatus', 'bulkApprove', 'bulkMarkPaid', 'approve', 'markPaid', 'exportPdf', 'exportToExcel',
    ]);
    apiSpy.list.and.returnValue(of(EMPTY_PAGE));
    storeMock = makeStoreMock();

    TestBed.configureTestingModule({
      imports: [PayoutsListComponent],
      providers: [
        // The component asks CurrentUserService whether the ids in the bulk-refusal banner may be
        // links, and that service injects HttpClient. Nothing here talks to the network; the
        // testing backend just makes the graph constructible.
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: PayoutsApiService, useValue: apiSpy },
        { provide: PayoutsStore,      useValue: storeMock },
        { provide: PayeesApiService,  useValue: jasmine.createSpyObj('PayeesApiService', ['getPayees']) },
        { provide: PlansApiService,   useValue: jasmine.createSpyObj('PlansApiService', ['getPlans', 'getPlan']) },
        { provide: ActivatedRoute,    useValue: { snapshot: { queryParams: {} }, queryParams: of({}) } },
        { provide: Router,            useValue: jasmine.createSpyObj('Router', ['navigate']) },
      ],
    });

    TestBed.overrideComponent(PayoutsListComponent, {
      set: { template: '<div></div>', imports: [] },
    });
  });

  it('does not reload in ngOnInit — refresh-on-entry is delegated to the shared [refreshOnEnter] directive', () => {
    const fixture = TestBed.createComponent(PayoutsListComponent);
    fixture.detectChanges(); // triggers ngOnInit (stubbed template has no directive)

    expect(storeMock.reload).not.toHaveBeenCalled();
  });
});

// ─── Export Excel ─────────────────────────────────────────────────────────────
describe('PayoutsListComponent — onExport', () => {
  let component: PayoutsListComponent;
  let apiSpy: jasmine.SpyObj<PayoutsApiService>;
  let storeMock: jasmine.SpyObj<PayoutsStore>;

  beforeEach(() => {
    apiSpy = jasmine.createSpyObj<PayoutsApiService>('PayoutsApiService', [
      'list', 'calculate', 'getJobStatus', 'bulkApprove', 'bulkMarkPaid', 'approve', 'markPaid', 'exportPdf', 'exportToExcel',
    ]);
    apiSpy.list.and.returnValue(of(EMPTY_PAGE));

    storeMock = makeStoreMock();

    TestBed.configureTestingModule({
      imports: [PayoutsListComponent],
      providers: [
        // The component asks CurrentUserService whether the ids in the bulk-refusal banner may be
        // links, and that service injects HttpClient. Nothing here talks to the network; the
        // testing backend just makes the graph constructible.
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: PayoutsApiService, useValue: apiSpy },
        { provide: PayoutsStore,      useValue: storeMock },
        { provide: PayeesApiService,  useValue: jasmine.createSpyObj('PayeesApiService', ['getPayees']) },
        { provide: PlansApiService,   useValue: jasmine.createSpyObj('PlansApiService', ['getPlans', 'getPlan']) },
        { provide: ActivatedRoute,    useValue: { snapshot: { queryParams: {} }, queryParams: of({}) } },
        { provide: Router,            useValue: jasmine.createSpyObj('Router', ['navigate']) },
      ],
    });

    TestBed.overrideComponent(PayoutsListComponent, {
      set: { template: '<div></div>', imports: [] },
    });

    const fixture = TestBed.createComponent(PayoutsListComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('calls exportToExcel with store export params and triggers blob download', async () => {
    const mockBlob = new Blob(['fake-xlsx'], { type: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet' });
    apiSpy.exportToExcel.and.returnValue(of(mockBlob));
    storeMock.totalCount.and.returnValue(5);

    const anchorSpy = jasmine.createSpyObj('a', ['click']);
    spyOn(document, 'createElement').and.returnValue(anchorSpy as unknown as HTMLAnchorElement);
    spyOn(document.body, 'appendChild');
    spyOn(document.body, 'removeChild');
    spyOn(URL, 'createObjectURL').and.returnValue('blob:fake');
    spyOn(URL, 'revokeObjectURL');

    await component.onExport();

    expect(apiSpy.exportToExcel).toHaveBeenCalledOnceWith({ excludeZero: 'true' });
    expect(anchorSpy.download).toMatch(/^payouts-export-\d{4}-\d{2}-\d{2}\.xlsx$/);
    expect(anchorSpy.click).toHaveBeenCalledTimes(1);
    expect(URL.revokeObjectURL).toHaveBeenCalledWith('blob:fake');
    expect(component.exporting()).toBe(false);
    expect(component.exportError()).toBeNull();
  });

  it('is a no-op when already exporting', async () => {
    component.exporting.set(true);
    await component.onExport();
    expect(apiSpy.exportToExcel).not.toHaveBeenCalled();
  });

  it('sets exportError and resets exporting on API failure', async () => {
    apiSpy.exportToExcel.and.returnValue(throwError(() => new Error('network')) as Observable<Blob>);
    storeMock.totalCount.and.returnValue(1);

    try { await component.onExport(); } catch { /* expected */ }

    expect(component.exportError()).toBe('PAYOUTS.EXPORT.ERROR');
    expect(component.exporting()).toBe(false);
  });
});

describe('PayoutsStore — bulkMarkPaidSummary', () => {
  let store: PayoutsStore;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        // The component asks CurrentUserService whether the ids in the bulk-refusal banner may be
        // links, and that service injects HttpClient. Nothing here talks to the network; the
        // testing backend just makes the graph constructible.
        provideHttpClient(),
        provideHttpClientTesting(),
        PayoutsStore,
        { provide: PayoutsApiService, useValue: jasmine.createSpyObj('PayoutsApiService', { list: of(EMPTY_PAGE) }) },
      ],
    });
    store = TestBed.inject(PayoutsStore);
  });

  it('exposes { payeeId, payeeName, payeeCode } for each Approved item in selection', () => {
    const items = [
      makePayoutListItem('p1', 'Approved',   'payee-1', 'Alice Smith', 'EMP-001', 500, 'EUR'),
      makePayoutListItem('p2', 'Approved',   'payee-2', 'Bob Jones',   'EMP-002', 200, 'USD'),
      makePayoutListItem('p3', 'Calculated', 'payee-3', 'Carol Wu',    'EMP-003'),
    ];
    store['pagedResult'].set({ items, totalCount: 3, page: 1, pageSize: 25, totalPages: 1, hasNextPage: false, hasPreviousPage: false, unfilteredTotal: undefined });
    store.selectedIds.set(new Set(['p1', 'p2', 'p3']));

    const summary = store.bulkMarkPaidSummary();

    expect(summary.count).toBe(2); // only Approved
    expect(summary.skippedCount).toBe(1); // Calculated is skipped
    expect(summary.payees).toEqual([
      { payeeId: 'payee-1', payeeName: 'Alice Smith', payeeCode: 'EMP-001', periodStart: '2026-01-01', periodEnd: '2026-03-31', amount: 500, currency: 'EUR' },
      { payeeId: 'payee-2', payeeName: 'Bob Jones',   payeeCode: 'EMP-002', periodStart: '2026-01-01', periodEnd: '2026-03-31', amount: 200, currency: 'USD' },
    ]);
    expect(summary.totalsByCurrency.get('EUR')).toBe(500);
    expect(summary.totalsByCurrency.get('USD')).toBe(200);
  });
});

// ── The filter has to follow the URL, not just the first render ──────────────
// Angular reuses this component when only the query params change, so a snapshot read in ngOnInit
// goes stale: arriving from the dashboard card kept the old filter until a manual reload, and
// leaving via the sidebar left the old filter applied under a URL that no longer mentioned it.
describe('PayoutsListComponent — query params after the first render', () => {
  let storeMock: jasmine.SpyObj<PayoutsStore>;
  let queryParams: BehaviorSubject<Record<string, string>>;
  let fixture: ComponentFixture<PayoutsListComponent>;

  beforeEach(() => {
    const apiSpy = jasmine.createSpyObj<PayoutsApiService>('PayoutsApiService', [
      'list', 'calculate', 'getJobStatus', 'bulkApprove', 'bulkMarkPaid', 'approve', 'markPaid',
      'exportPdf', 'exportToExcel',
    ]);
    apiSpy.list.and.returnValue(of(EMPTY_PAGE));
    storeMock = makeStoreMock();
    queryParams = new BehaviorSubject<Record<string, string>>({ status: 'Approved' });

    TestBed.configureTestingModule({
      imports: [PayoutsListComponent],
      providers: [
        // The component asks CurrentUserService whether the ids in the bulk-refusal banner may be
        // links, and that service injects HttpClient. Nothing here talks to the network; the
        // testing backend just makes the graph constructible.
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: PayoutsApiService, useValue: apiSpy },
        { provide: PayoutsStore,      useValue: storeMock },
        { provide: PayeesApiService,  useValue: jasmine.createSpyObj('PayeesApiService', ['getPayees']) },
        { provide: PlansApiService,   useValue: jasmine.createSpyObj('PlansApiService', ['getPlans', 'getPlan']) },
        { provide: ActivatedRoute,    useValue: { snapshot: { queryParams: {} }, queryParams } },
        { provide: Router,            useValue: jasmine.createSpyObj('Router', ['navigate']) },
      ],
    });
    TestBed.overrideComponent(PayoutsListComponent, { set: { template: '<div></div>', imports: [] } });
    fixture = TestBed.createComponent(PayoutsListComponent);
    fixture.detectChanges();
  });

  it('applies the filter the URL carried on arrival', () => {
    expect(storeMock.loadFromQueryParams).toHaveBeenCalledWith({ status: 'Approved' });
  });

  it('re-applies when the URL changes without the component being recreated', () => {
    storeMock.loadFromQueryParams.calls.reset();

    queryParams.next({ status: 'Paid' });

    expect(storeMock.loadFromQueryParams).toHaveBeenCalledWith({ status: 'Paid' });
  });

  it('clears the filter when the URL loses its params', () => {
    // Leaving via the sidebar's Payouts link: no params means the default view, not the leftover one.
    storeMock.clearFilters.calls.reset();

    queryParams.next({});

    expect(storeMock.clearFilters).toHaveBeenCalled();
  });

  it('stops listening once the component is destroyed', () => {
    // A subscription that outlives the component would keep writing into a store the user has
    // navigated away from — and would pile up one more listener per visit.
    fixture.destroy();
    storeMock.loadFromQueryParams.calls.reset();

    queryParams.next({ status: 'Calculated' });

    expect(storeMock.loadFromQueryParams).not.toHaveBeenCalled();
    expect(queryParams.observed).withContext('no listener left behind').toBeFalse();
  });
});
