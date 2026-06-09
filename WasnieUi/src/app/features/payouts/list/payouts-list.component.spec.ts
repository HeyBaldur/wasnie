import { TestBed, fakeAsync, tick } from '@angular/core/testing';
import { ActivatedRoute, Router } from '@angular/router';
import { Observable, of, throwError } from 'rxjs';

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
    'loadFromQueryParams', 'toQueryParams', 'clearSelection',
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
  m.reload.and.returnValue(Promise.resolve());
  return m;
}

// ─── Regression: _pollJob infinite loop ──────────────────────────────────────
//
// Bug: after a Calculate job reached Succeeded, _pollJob kept calling
// getJobStatus every 2 s indefinitely (no takeWhile/takeUntil on terminal
// state), and _onJobDone + store.reload fired on every tick.
//
// Fix: takeWhile(s => Pending|Running, inclusive=true) completes the observable
// the moment a terminal state is received.
//
// These tests verify that the polling stops and reload fires exactly once.
// ─────────────────────────────────────────────────────────────────────────────

describe('PayoutsListComponent — poll-loop regression', () => {
  let component: PayoutsListComponent;
  let apiSpy: jasmine.SpyObj<PayoutsApiService>;
  let storeMock: jasmine.SpyObj<PayoutsStore>;

  beforeEach(() => {
    apiSpy = jasmine.createSpyObj<PayoutsApiService>('PayoutsApiService', [
      'list', 'calculate', 'getJobStatus', 'bulkApprove', 'bulkMarkPaid', 'approve', 'markPaid', 'exportPdf',
    ]);
    apiSpy.list.and.returnValue(of(EMPTY_PAGE));

    storeMock = makeStoreMock();

    TestBed.configureTestingModule({
      imports: [PayoutsListComponent],
      providers: [
        { provide: PayoutsApiService, useValue: apiSpy },
        { provide: PayoutsStore,      useValue: storeMock },
        { provide: PayeesApiService,  useValue: jasmine.createSpyObj('PayeesApiService', ['getPayees']) },
        { provide: PlansApiService,   useValue: jasmine.createSpyObj('PlansApiService', ['getPlans', 'getPlan']) },
        { provide: ActivatedRoute,    useValue: { snapshot: { queryParams: {} } } },
        { provide: Router,            useValue: jasmine.createSpyObj('Router', ['navigate']) },
      ],
    });

    // Strip all standalone imports and the template so the test focuses purely
    // on component class behaviour, without needing every UI primitive.
    TestBed.overrideComponent(PayoutsListComponent, {
      set: { template: '<div></div>', imports: [] },
    });

    const fixture = TestBed.createComponent(PayoutsListComponent);
    component = fixture.componentInstance;
    fixture.detectChanges(); // runs ngOnInit
  });

  it('stops polling after Succeeded and calls store.reload exactly once', fakeAsync(() => {
    let callCount = 0;
    apiSpy.getJobStatus.and.callFake(() => {
      callCount++;
      const state = callCount < 3 ? 'Pending' : 'Succeeded';
      return of({
        id: 'job-abc',
        state,
        errorMessage: null,
        resultSummary: '{"payoutsCreated":2,"conflicts":[],"warnings":[]}',
      });
    });

    (component as any)._pollJob('job-abc');

    tick(2000);  // poll 1 → Pending  — no reload
    tick(2000);  // poll 2 → Pending  — no reload
    tick(2000);  // poll 3 → Succeeded — _onJobDone → store.reload, observable completes
    tick(20000); // extra time — must NOT trigger any more polls

    expect(apiSpy.getJobStatus).toHaveBeenCalledTimes(3);
    expect(storeMock.reload).toHaveBeenCalledTimes(1);
  }));

  it('stops polling on Failed without calling store.reload', fakeAsync(() => {
    apiSpy.getJobStatus.and.returnValue(
      of({ id: 'job-xyz', state: 'Failed', errorMessage: 'PAYOUTS.CALCULATE_ERROR', resultSummary: null })
    );

    (component as any)._pollJob('job-xyz');

    tick(2000);  // poll 1 → Failed — observable completes
    tick(20000); // extra time — must NOT trigger any more polls

    expect(apiSpy.getJobStatus).toHaveBeenCalledTimes(1);
    expect(storeMock.reload).not.toHaveBeenCalled();
  }));

  it('stops polling on Cancelled without calling store.reload', fakeAsync(() => {
    apiSpy.getJobStatus.and.returnValue(
      of({ id: 'job-xyz', state: 'Cancelled', errorMessage: null, resultSummary: null })
    );

    (component as any)._pollJob('job-xyz');

    tick(2000);
    tick(20000);

    expect(apiSpy.getJobStatus).toHaveBeenCalledTimes(1);
    expect(storeMock.reload).not.toHaveBeenCalled();
  }));
});

// ─── Bulk Mark as Paid ───────────────────────────────────────────────────────
describe('PayoutsListComponent — onBulkMarkPaid', () => {
  let component: PayoutsListComponent;
  let apiSpy: jasmine.SpyObj<PayoutsApiService>;
  let storeMock: jasmine.SpyObj<PayoutsStore>;

  beforeEach(() => {
    apiSpy = jasmine.createSpyObj<PayoutsApiService>('PayoutsApiService', [
      'list', 'calculate', 'getJobStatus', 'bulkApprove', 'bulkMarkPaid', 'approve', 'markPaid', 'exportPdf',
    ]);
    apiSpy.list.and.returnValue(of(EMPTY_PAGE));

    storeMock = makeStoreMock();

    TestBed.configureTestingModule({
      imports: [PayoutsListComponent],
      providers: [
        { provide: PayoutsApiService, useValue: apiSpy },
        { provide: PayoutsStore,      useValue: storeMock },
        { provide: PayeesApiService,  useValue: jasmine.createSpyObj('PayeesApiService', ['getPayees']) },
        { provide: PlansApiService,   useValue: jasmine.createSpyObj('PlansApiService', ['getPlans', 'getPlan']) },
        { provide: ActivatedRoute,    useValue: { snapshot: { queryParams: {} } } },
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
  };
}

describe('PayoutsStore — bulkApproveSummary', () => {
  let store: PayoutsStore;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
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

describe('PayoutsStore — bulkMarkPaidSummary', () => {
  let store: PayoutsStore;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
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
