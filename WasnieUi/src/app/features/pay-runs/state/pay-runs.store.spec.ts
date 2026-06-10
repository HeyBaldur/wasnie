import { TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { PayRunsStore, EMPTY_PAY_RUN_FILTER, PayRunFilter } from './pay-runs.store';
import { PayRunsApiService } from '../services/pay-runs.api.service';
import { PayRunListItem } from '../models/pay-run.model';

const makeRun = (id: string, status: PayRunListItem['status'] = 'Draft'): PayRunListItem => ({
  id,
  periodStart: '2026-06-01',
  periodEnd: '2026-06-30',
  status,
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

  it('should have page=1 and pageSize=25 by default', () => {
    expect(store.page()).toBe(1);
    expect(store.pageSize()).toBe(25);
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
});
