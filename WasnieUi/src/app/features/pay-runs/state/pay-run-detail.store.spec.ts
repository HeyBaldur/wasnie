import { TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { PayRunDetailStore } from './pay-run-detail.store';
import { PayRunsApiService } from '../services/pay-runs.api.service';
import { PayRunDetail } from '../models/pay-run.model';
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

  it('should have excludeZero=true by default', () => {
    expect(store.excludeZero()).toBeTrue();
  });

  // ── load ──────────────────────────────────────────────────────────────────

  it('load sets run and clears error', async () => {
    await store.load('run-1');

    expect(store.run()?.id).toBe('run-1');
    expect(store.error()).toBeNull();
    expect(store.loading()).toBeFalse();
  });

  it('load calls getById with runId, page, pageSize, excludeZero', async () => {
    await store.load('run-abc');

    expect(apiSpy.getById).toHaveBeenCalledOnceWith('run-abc', 1, 25, true);
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
    expect(s.count).toBe(8); // paidPayeeCount from makeRun()
  });

  it('markPaidSummary exposes zeroPayoutCount as skippedCount', async () => {
    await store.load('run-1');
    const s = store.markPaidSummary();
    expect(s.skippedCount).toBe(2); // zeroPayoutCount from makeRun()
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

  // ── excludeZero toggle ────────────────────────────────────────────────────

  it('setExcludeZero calls getById with new excludeZero value and resets page to 1', async () => {
    await store.load('run-1');
    store.setPage(3); // advance to page 3 manually (simulated, doesn't call _fetch again without await)
    store.page.set(3); // set directly since setPage also calls _fetch
    apiSpy.getById.calls.reset();

    store.setExcludeZero(false);
    // Wait for the async _fetch to complete
    await new Promise(r => setTimeout(r, 0));

    expect(store.excludeZero()).toBeFalse();
    expect(store.page()).toBe(1);
    expect(apiSpy.getById).toHaveBeenCalledWith('run-1', 1, 25, false);
  });

  // ── reload fires exactly once after action ───────────────────────────────
  // Regression guard: actions must call reload() once, not in a loop.

  it('reload calls getById exactly once', async () => {
    await store.load('run-1');
    apiSpy.getById.calls.reset();

    await store.reload();

    expect(apiSpy.getById).toHaveBeenCalledTimes(1);
  });
});
