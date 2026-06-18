import { TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { PayRunDetailStore } from './pay-run-detail.store';
import { PayRunsApiService } from '../services/pay-runs.api.service';
import { EMPTY_PAYOUTS_DETAIL_FILTER, PayRunDetail } from '../models/pay-run.model';
import { PayoutListItem } from '../../payouts/models/payout.model';

const makePagedPayouts = (items: PayoutListItem[] = []) => ({
  items,
  totalCount: items.length,
  page: 1,
  pageSize: 25,
  totalPages: 1,
  hasNextPage: false,
  hasPreviousPage: false,
  unfilteredTotal: undefined,
});

const makeRun = (overrides: Partial<PayRunDetail> = {}): PayRunDetail => ({
  id: 'run-1',
  periodStart: '2026-06-01',
  periodEnd: '2026-06-30',
  status: 'Draft',
  payeeCount: 10,
  paidPayeeCount: 8,
  zeroPayoutCount: 2,
  totalAmounts: { EUR: 5000, PLN: 3000 },
  createdAt: '2026-06-10T00:00:00Z',
  createdBy: 'admin',
  approvedAt: null,
  approvedBy: null,
  paidAt: null,
  paidBy: null,
  payouts: makePagedPayouts(),
  ...overrides,
});

describe('PayRunDetailStore', () => {
  let store: PayRunDetailStore;
  let apiSpy: jasmine.SpyObj<PayRunsApiService>;

  beforeEach(() => {
    apiSpy = jasmine.createSpyObj('PayRunsApiService', ['getById']);
    apiSpy.getById.and.returnValue(of(makeRun()));

    TestBed.configureTestingModule({
      providers: [
        PayRunDetailStore,
        { provide: PayRunsApiService, useValue: apiSpy },
      ],
    });
    store = TestBed.inject(PayRunDetailStore);
  });

  // ── Initial state ─────────────────────────────────────────────────────────

  it('should start with null run', () => {
    expect(store.run()).toBeNull();
    expect(store.status()).toBeNull();
  });

  it('should have excludeZero=true by default (from EMPTY_PAYOUTS_DETAIL_FILTER)', () => {
    expect(store.excludeZero()).toBeTrue();
  });

  it('should start with empty filter and zero activeFilterCount', () => {
    expect(store.activeFilterCount()).toBe(0);
  });

  // ── load ──────────────────────────────────────────────────────────────────

  it('load sets run and clears error', async () => {
    await store.load('run-1');

    expect(store.run()?.id).toBe('run-1');
    expect(store.error()).toBeNull();
    expect(store.loading()).toBeFalse();
  });

  it('load calls getById with runId, filter, page, pageSize', async () => {
    await store.load('run-abc');

    expect(apiSpy.getById).toHaveBeenCalledOnceWith(
      'run-abc', EMPTY_PAYOUTS_DETAIL_FILTER, 1, 10
    );
  });

  it('error is set when load fails', async () => {
    apiSpy.getById.and.returnValue(throwError(() => new Error('fail')));
    await store.load('run-1');

    expect(store.error()).toBe('ERRORS.GENERIC');
    expect(store.loading()).toBeFalse();
  });

  // ── Status computed flags ─────────────────────────────────────────────────

  it('isDraft is true for Draft status', async () => {
    await store.load('run-1');
    expect(store.isDraft()).toBeTrue();
    expect(store.isApproved()).toBeFalse();
    expect(store.isPaid()).toBeFalse();
  });

  it('isApproved is true for Approved status', async () => {
    apiSpy.getById.and.returnValue(of(makeRun({ status: 'Approved' })));
    await store.load('run-1');
    expect(store.isApproved()).toBeTrue();
    expect(store.isDraft()).toBeFalse();
  });

  it('isPaid is true for Paid status', async () => {
    apiSpy.getById.and.returnValue(of(makeRun({ status: 'Paid' })));
    await store.load('run-1');
    expect(store.isPaid()).toBeTrue();
  });

  // ── markPaidSummary ───────────────────────────────────────────────────────

  it('markPaidSummary exposes paidPayeeCount as count', async () => {
    await store.load('run-1');
    const s = store.markPaidSummary();
    expect(s.count).toBe(8);
  });

  it('markPaidSummary exposes zeroPayoutCount as skippedCount', async () => {
    await store.load('run-1');
    const s = store.markPaidSummary();
    expect(s.skippedCount).toBe(2);
  });

  it('markPaidSummary exposes per-currency totalAmounts', async () => {
    await store.load('run-1');
    const s = store.markPaidSummary();
    expect(s.totalAmounts).toContain(jasmine.objectContaining({ currency: 'EUR', amount: 5000 }));
    expect(s.totalAmounts).toContain(jasmine.objectContaining({ currency: 'PLN', amount: 3000 }));
  });

  it('markPaidSummary returns zeros before run is loaded', () => {
    const s = store.markPaidSummary();
    expect(s.count).toBe(0);
    expect(s.totalAmounts).toEqual([]);
    expect(s.skippedCount).toBe(0);
  });

  // ── totalAmountsEntries ───────────────────────────────────────────────────

  it('totalAmountsEntries maps run.totalAmounts to { currency, amount } array', async () => {
    await store.load('run-1');
    const entries = store.totalAmountsEntries();
    expect(entries).toContain(jasmine.objectContaining({ currency: 'EUR', amount: 5000 }));
    expect(entries).toContain(jasmine.objectContaining({ currency: 'PLN', amount: 3000 }));
  });

  // ── excludeZero / setExcludeZero ──────────────────────────────────────────

  it('setExcludeZero updates excludeZero flag and refetches with page 1', async () => {
    await store.load('run-1');
    store.page.set(3);
    apiSpy.getById.calls.reset();

    store.setExcludeZero(false);
    await new Promise(r => setTimeout(r, 0));

    expect(store.excludeZero()).toBeFalse();
    expect(store.page()).toBe(1);
    const call = apiSpy.getById.calls.mostRecent();
    expect(call.args[0]).toBe('run-1');
    expect((call.args[1] as { excludeZero: boolean }).excludeZero).toBeFalse();
  });

  // ── setFilter ─────────────────────────────────────────────────────────────

  it('setFilter updates filter fields and increments activeFilterCount', async () => {
    await store.load('run-1');
    apiSpy.getById.calls.reset();

    store.setFilter({ status: 'Approved', periodFrom: '2026-01-01' });
    await new Promise(r => setTimeout(r, 0));

    expect(store.filter().status).toBe('Approved');
    expect(store.filter().periodFrom).toBe('2026-01-01');
    expect(store.activeFilterCount()).toBe(2);
    expect(apiSpy.getById).toHaveBeenCalledTimes(1);
  });

  it('clearFilters resets all fields and calls getById once', async () => {
    await store.load('run-1');
    store.setFilter({ status: 'Paid', amountMin: 100 });
    await new Promise(r => setTimeout(r, 0));
    apiSpy.getById.calls.reset();

    store.clearFilters();
    await new Promise(r => setTimeout(r, 0));

    expect(store.activeFilterCount()).toBe(0);
    expect(store.filter().status).toBeNull();
    expect(apiSpy.getById).toHaveBeenCalledTimes(1);
  });

  // ── toExportParams — race-condition guard ─────────────────────────────────

  it('toExportParams returns params from last loaded filter, not in-flight filter()', async () => {
    await store.load('run-1');
    // Simulate an in-flight filter change that hasn't completed a load yet.
    store.filter.set({ ...EMPTY_PAYOUTS_DETAIL_FILTER, status: 'Approved' });

    // _lastLoadedFilter still has the original (empty) filter.
    const params = store.toExportParams();
    // status from _lastLoadedFilter (empty) should not appear.
    expect(params['status']).toBeUndefined();
  });

  it('toExportParams falls back to filter() when no load has completed', () => {
    store.filter.set({ ...EMPTY_PAYOUTS_DETAIL_FILTER, status: 'Paid' });
    const params = store.toExportParams();
    expect(params['status']).toBe('Paid');
  });

  it('toExportParams includes excludeZero=true when set', async () => {
    await store.load('run-1');
    const params = store.toExportParams();
    expect(params['excludeZero']).toBe('true');
  });

  // ── reload fires exactly once ─────────────────────────────────────────────

  it('reload calls getById exactly once', async () => {
    await store.load('run-1');
    apiSpy.getById.calls.reset();

    await store.reload();

    expect(apiSpy.getById).toHaveBeenCalledTimes(1);
  });
});
