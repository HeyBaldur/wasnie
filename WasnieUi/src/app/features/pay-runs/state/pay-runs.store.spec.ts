import { TestBed } from '@angular/core/testing';
import { of, throwError, Subject } from 'rxjs';
import { PayRunsStore, EMPTY_PAY_RUN_FILTER, PayRunFilter } from './pay-runs.store';
import { PayRunsApiService } from '../services/pay-runs.api.service';
import { PayRunListItem } from '../models/pay-run.model';

const makeRun = (id: string, status: PayRunListItem['status'] = 'Draft'): PayRunListItem => ({
  id,
  periodStart: '2026-06-01',
  periodEnd: '2026-06-30',
  status,
  supplementalSequence: 0,
  payeeCount: 10,
  paidPayeeCount: 8,
  zeroPayoutCount: 2,
  totalAmounts: { EUR: 5000 },
  createdAt: '2026-06-10T00:00:00Z',
  createdBy: 'admin',
  approvedAt: null,
  approvedBy: null,
  paidAt: null,
  paidBy: null,
});

const makePaged = (items: PayRunListItem[]) => ({
  items,
  totalCount: items.length,
  page: 1,
  pageSize: 25,
  totalPages: 1,
  hasNextPage: false,
  hasPreviousPage: false,
  unfilteredTotal: undefined,
});

describe('PayRunsStore', () => {
  let store: PayRunsStore;
  let apiSpy: jasmine.SpyObj<PayRunsApiService>;

  beforeEach(() => {
    apiSpy = jasmine.createSpyObj('PayRunsApiService', ['list']);
    apiSpy.list.and.returnValue(of(makePaged([])));

    TestBed.configureTestingModule({
      providers: [
        PayRunsStore,
        { provide: PayRunsApiService, useValue: apiSpy },
      ],
    });
    store = TestBed.inject(PayRunsStore);
  });

  // ── Initial state ─────────────────────────────────────────────────────────

  it('should start with empty items', () => {
    expect(store.items()).toEqual([]);
    expect(store.totalCount()).toBe(0);
  });

  it('should have page=1 and pageSize=10 by default', () => {
    expect(store.page()).toBe(1);
    expect(store.pageSize()).toBe(10);
  });

  it('should start with EMPTY_PAY_RUN_FILTER', () => {
    expect(store.filter()).toEqual(EMPTY_PAY_RUN_FILTER);
  });

  // ── Filter ────────────────────────────────────────────────────────────────

  it('setFilter merges partial filter and resets to page 1', () => {
    store.setPage(3);
    store.setFilter({ status: 'Approved' });

    expect(store.filter().status).toBe('Approved');
    expect(store.page()).toBe(1);
  });

  it('clearFilters resets to EMPTY_PAY_RUN_FILTER', () => {
    store.setFilter({ status: 'Paid', periodFrom: '2026-01-01' });
    store.clearFilters();

    expect(store.filter()).toEqual(EMPTY_PAY_RUN_FILTER);
  });

  it('setPageSize updates pageSize and resets to page 1', () => {
    store.setPage(3);
    store.setPageSize(10);

    expect(store.pageSize()).toBe(10);
    expect(store.page()).toBe(1);
  });

  // ── _buildFilterRecord ────────────────────────────────────────────────────

  it('_buildFilterRecord emits status when not All', () => {
    const r = PayRunsStore._buildFilterRecord({ ...EMPTY_PAY_RUN_FILTER, status: 'Draft' });
    expect(r['status']).toBe('Draft');
  });

  it('_buildFilterRecord omits status when All', () => {
    const r = PayRunsStore._buildFilterRecord({ ...EMPTY_PAY_RUN_FILTER, status: 'All' });
    expect(r['status']).toBeUndefined();
  });

  it('_buildFilterRecord includes periodFrom and periodTo', () => {
    const r = PayRunsStore._buildFilterRecord({
      ...EMPTY_PAY_RUN_FILTER,
      periodFrom: '2026-01-01',
      periodTo: '2026-01-31',
    });
    expect(r['periodFrom']).toBe('2026-01-01');
    expect(r['periodTo']).toBe('2026-01-31');
  });

  it('_buildFilterRecord returns empty object for empty filter', () => {
    const r = PayRunsStore._buildFilterRecord(EMPTY_PAY_RUN_FILTER);
    expect(Object.keys(r)).toEqual([]);
  });

  // ── _lastLoadedFilter race-condition guard ────────────────────────────────

  it('lastLoadedFilter returns null before first load completes', () => {
    // The store is freshly created; if the effect hasn't fired yet, lastLoadedFilter is null.
    // (TestBed.inject triggers the effect, but it resolves the promise async.)
    // We can only confirm the value AFTER reload.
    expect(store.lastLoadedFilter()).toBeNull();
  });

  it('lastLoadedFilter captures the filter at the time of the last completed load', async () => {
    apiSpy.list.and.returnValue(of(makePaged([makeRun('r1')])));
    store.setFilter({ status: 'Draft', periodFrom: '2026-06-01', periodTo: '2026-06-30' });
    await store.reload();

    const last = store.lastLoadedFilter();
    expect(last?.status).toBe('Draft');
    expect(last?.periodFrom).toBe('2026-06-01');
  });

  it('lastLoadedFilter is NOT updated when filter changes before the next reload', async () => {
    apiSpy.list.and.returnValue(of(makePaged([makeRun('r1')])));
    store.setFilter({ periodFrom: '2026-01-01', periodTo: '2026-01-31' });
    await store.reload(); // _lastLoadedFilter = January

    // Synchronously change the filter; new load has NOT completed.
    store.setFilter({ periodFrom: '2026-06-01', periodTo: '2026-06-30' });

    // lastLoadedFilter must still be January.
    const last = store.lastLoadedFilter();
    expect(last?.periodFrom).toBe('2026-01-01');
  });

  // ── reload fires exactly once ─────────────────────────────────────────────

  it('reload calls api.list exactly once', async () => {
    apiSpy.list.and.returnValue(of(makePaged([])));
    const callsBefore = apiSpy.list.calls.count();
    await store.reload();
    expect(apiSpy.list.calls.count()).toBe(callsBefore + 1);
  });

  // ── activeFilterCount ────────────────────────────────────────────────────

  it('activeFilterCount is 0 for empty filter', () => {
    expect(store.activeFilterCount()).toBe(0);
  });

  it('activeFilterCount increments for status', () => {
    store.setFilter({ status: 'Draft' });
    expect(store.activeFilterCount()).toBe(1);
  });

  it('activeFilterCount increments for period (one field counts as one)', () => {
    store.setFilter({ periodFrom: '2026-01-01' });
    expect(store.activeFilterCount()).toBe(1);
    store.setFilter({ periodTo: '2026-12-31' });
    expect(store.activeFilterCount()).toBe(1); // from + to = still 1 "period" filter
  });

  it('activeFilterCount is 2 for status + period', () => {
    store.setFilter({ status: 'Paid', periodFrom: '2026-01-01' });
    expect(store.activeFilterCount()).toBe(2);
  });

  // ── toExportParams ────────────────────────────────────────────────────────

  it('toExportParams falls back to filter() when no load has completed yet', () => {
    store.setFilter({ status: 'Approved' });
    // No completed load → falls back to filter()
    const params = store.toExportParams();
    expect(params['status']).toBe('Approved');
  });

  it('toExportParams uses _lastLoadedFilter after a completed load', async () => {
    apiSpy.list.and.returnValue(of(makePaged([makeRun('r1')])));
    store.setFilter({ status: 'Draft', periodFrom: '2026-06-01', periodTo: '2026-06-30' });
    await store.reload();

    // Now change filter without completing a load.
    store.setFilter({ status: 'Paid' });

    // toExportParams should still read the completed-load filter.
    const params = store.toExportParams();
    expect(params['status']).toBe('Draft');
    expect(params['periodFrom']).toBe('2026-06-01');
  });

  // ── Error handling ────────────────────────────────────────────────────────

  it('error signal is set when API call fails', async () => {
    apiSpy.list.and.returnValue(throwError(() => new Error('Network error')));
    await store.reload();

    expect(store.error()).toBe('ERRORS.GENERIC');
  });

  it('loading resets to false after error', async () => {
    apiSpy.list.and.returnValue(throwError(() => new Error('fail')));
    await store.reload();

    expect(store.loading()).toBeFalse();
  });
  // ── Stale-response guard (LatestRequestGuard) ─────────────────────────────
  //
  // The "I have to press F5" bug, at store level. Change the filter while a fetch is still in flight
  // and two requests race. Before the guard, whichever the network returned LAST won: when the slow
  // one was the older, wider query, the list showed every pay run under a Draft filter and stayed
  // that way until a manual reload. Reproduced deterministically on 2026-08-18 (WI-1 Paso 0.2).
  // ──────────────────────────────────────────────────────────────────────────
  describe('stale-response guard', () => {
    it('discards the older response when it arrives after the newer one', async () => {
      const wide = new Subject<ReturnType<typeof makePaged>>();
      const narrow = new Subject<ReturnType<typeof makePaged>>();
      apiSpy.list.and.returnValues(wide.asObservable(), narrow.asObservable());

      const wideLoad = store.reload();                       // request 1 — unfiltered, slow
      const narrowLoad = store.reload();                     // request 2 — supersedes it

      narrow.next(makePaged([makeRun('narrow')]));           // newer ARRIVES first
      narrow.complete();
      await narrowLoad;

      wide.next(makePaged([makeRun('a'), makeRun('b')]));    // older ARRIVES last
      wide.complete();
      await wideLoad;

      expect(store.items().map(r => r.id)).toEqual(['narrow']);
    });

    it('does not let a stale FAILURE clobber a fresh result', async () => {
      const failing = new Subject<ReturnType<typeof makePaged>>();
      const ok = new Subject<ReturnType<typeof makePaged>>();
      apiSpy.list.and.returnValues(failing.asObservable(), ok.asObservable());

      const failingLoad = store.reload();
      const okLoad = store.reload();

      ok.next(makePaged([makeRun('fresh')]));
      ok.complete();
      await okLoad;

      failing.error(new Error('stale failure'));             // older request fails last
      await failingLoad;

      expect(store.error()).toBeNull();
      expect(store.items().map(r => r.id)).toEqual(['fresh']);
    });

    it('a stale request finishing does not clear the spinner of the live one', async () => {
      const stale = new Subject<ReturnType<typeof makePaged>>();
      const live = new Subject<ReturnType<typeof makePaged>>();
      apiSpy.list.and.returnValues(stale.asObservable(), live.asObservable());

      const staleLoad = store.reload();
      void store.reload();                                   // live request, still in flight

      stale.next(makePaged([]));                             // stale one finishes first
      stale.complete();
      await staleLoad;

      expect(store.loading()).toBeTrue();

      live.next(makePaged([]));
      live.complete();
    });
  });
});
