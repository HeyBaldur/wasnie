import { TestBed, fakeAsync, tick } from '@angular/core/testing';
import { ActivatedRoute, Router } from '@angular/router';
import { of } from 'rxjs';

import { PayoutsListComponent } from './payouts-list.component';
import { PayoutsApiService } from '../services/payouts.api.service';
import { PayoutsStore, EMPTY_PAYOUT_FILTER } from '../state/payouts.store';
import { PayeesApiService } from '../../payees/services/payees.api.service';
import { PlansApiService } from '../../plans/services/plans.api.service';
import { PagedResult } from '../../../shared/models/pagination.models';
import { PayoutListItem } from '../models/payout.model';

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

function makeStoreMock(): jasmine.SpyObj<PayoutsStore> {
  const m = jasmine.createSpyObj<PayoutsStore>('PayoutsStore', [
    'setFilter', 'clearFilters', 'setPage', 'setPageSize',
    'loadFromQueryParams', 'toQueryParams', 'clearSelection',
    'toggleSelect', 'toggleSelectAll', 'reload',
    // signal-like accessors (called as functions in the component)
    'filter', 'items', 'loading', 'error', 'selectedIds',
    'selectedCalculatedIds', 'hasActiveFilters', 'activeFilterCount',
    'selectedCount', 'allSelected', 'page', 'pageSize', 'totalCount', 'totalPages',
  ]);
  m.filter.and.returnValue({ ...EMPTY_PAYOUT_FILTER });
  m.items.and.returnValue([]);
  m.loading.and.returnValue(false);
  m.error.and.returnValue(null);
  m.selectedIds.and.returnValue(new Set<string>());
  m.selectedCalculatedIds.and.returnValue([]);
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
      'list', 'calculate', 'getJobStatus', 'bulkApprove', 'approve', 'markPaid', 'exportPdf',
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
