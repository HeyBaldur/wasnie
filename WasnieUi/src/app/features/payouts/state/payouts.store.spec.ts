import { TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { PayoutsStore, EMPTY_PAYOUT_FILTER, PayoutFilter } from './payouts.store';
import { PayoutsApiService } from '../services/payouts.api.service';
import { PayoutListItem } from '../models/payout.model';

const makeItem = (id: string, status: PayoutListItem['status'] = 'Calculated'): PayoutListItem => ({
  id,
  payeeId: 'p1',
  payeeName: 'Test',
  payeeCode: 'TST',
  planId: 'pl1',
  planName: 'Plan A',
  periodStart: '2026-01-01',
  periodEnd: '2026-03-31',
  totalCommissionAmount: 100,
  totalCommissionCurrency: 'EUR',
  status,
  calculatedAt: '2026-06-01T00:00:00Z',
  calculatedBy: 'system',
  updatedAt: '2026-06-01T00:00:00Z',
  updatedBy: 'system',
  paidAt: null,
});

const makePaged = (items: PayoutListItem[]) => ({
  items,
  totalCount: items.length,
  page: 1,
  pageSize: 25,
  totalPages: 1,
  hasNextPage: false,
  hasPreviousPage: false,
  unfilteredTotal: undefined,
});

describe('PayoutsStore', () => {
  let store: PayoutsStore;
  let apiSpy: jasmine.SpyObj<PayoutsApiService>;

  beforeEach(() => {
    apiSpy = jasmine.createSpyObj('PayoutsApiService', ['list', 'bulkApprove']);
    apiSpy.list.and.returnValue(of(makePaged([])));

    TestBed.configureTestingModule({
      providers: [
        PayoutsStore,
        { provide: PayoutsApiService, useValue: apiSpy },
      ],
    });
    store = TestBed.inject(PayoutsStore);
  });

  // ── Initial state ─────────────────────────────────────────────────────────

  it('should start with empty items and no active filters', () => {
    expect(store.items()).toEqual([]);
    expect(store.hasActiveFilters()).toBeFalse();
    expect(store.selectedCount()).toBe(0);
  });

  it('should have page=1 and pageSize=10 by default', () => {
    expect(store.page()).toBe(1);
    expect(store.pageSize()).toBe(10);
  });

  // ── Filter ────────────────────────────────────────────────────────────────

  it('setFilter should merge partial filter and reset to page 1', () => {
    store.setPage(3);
    store.setFilter({ status: 'Approved' });

    expect(store.filter().status).toBe('Approved');
    expect(store.page()).toBe(1);
  });

  it('clearFilters should reset to EMPTY_PAYOUT_FILTER', () => {
    store.setFilter({ status: 'Paid', currencies: ['EUR'] });
    store.clearFilters();

    expect(store.filter()).toEqual(EMPTY_PAYOUT_FILTER);
    expect(store.hasActiveFilters()).toBeFalse();
  });

  it('activeFilterCount counts each active filter group', () => {
    store.setFilter({ payeeIds: ['a', 'b'], status: 'Calculated', currencies: ['EUR'] });
    expect(store.activeFilterCount()).toBe(3);
  });

  // ── Selection ─────────────────────────────────────────────────────────────

  it('toggleSelect should add an id to the selection', async () => {
    apiSpy.list.and.returnValue(of(makePaged([makeItem('x1'), makeItem('x2')])));
    await store.reload();

    store.toggleSelect('x1');
    expect(store.selectedIds().has('x1')).toBeTrue();
    expect(store.selectedCount()).toBe(1);
  });

  it('toggleSelect should remove an already-selected id', async () => {
    apiSpy.list.and.returnValue(of(makePaged([makeItem('x1')])));
    await store.reload();

    store.toggleSelect('x1');
    store.toggleSelect('x1');
    expect(store.selectedIds().has('x1')).toBeFalse();
  });

  it('toggleSelectAll should select all visible items', async () => {
    apiSpy.list.and.returnValue(of(makePaged([makeItem('a'), makeItem('b')])));
    await store.reload();

    store.toggleSelectAll();
    expect(store.allSelected()).toBeTrue();
    expect(store.selectedCount()).toBe(2);
  });

  it('toggleSelectAll when all selected should deselect all', async () => {
    apiSpy.list.and.returnValue(of(makePaged([makeItem('a')])));
    await store.reload();

    store.toggleSelectAll();
    store.toggleSelectAll();
    expect(store.selectedCount()).toBe(0);
  });

  it('selectedCalculatedIds returns only Calculated selected items', async () => {
    apiSpy.list.and.returnValue(of(makePaged([
      makeItem('c1', 'Calculated'),
      makeItem('a1', 'Approved'),
    ])));
    await store.reload();

    store.toggleSelectAll();
    expect(store.selectedCalculatedIds()).toEqual(['c1']);
  });

  it('clearSelection empties the selection', async () => {
    apiSpy.list.and.returnValue(of(makePaged([makeItem('a')])));
    await store.reload();
    store.toggleSelectAll();

    store.clearSelection();
    expect(store.selectedCount()).toBe(0);
  });

  // ── toExportParams — race-condition guard ─────────────────────────────────

  it('toExportParams returns filter used for the last completed load', async () => {
    apiSpy.list.and.returnValue(of(makePaged([makeItem('p1')])));
    store.setFilter({ periodFrom: '2026-01-01', periodTo: '2026-01-31' });
    await store.reload();

    const params = store.toExportParams();

    expect(params['periodFrom']).toBe('2026-01-01');
    expect(params['periodTo']).toBe('2026-01-31');
  });

  it('toExportParams keeps last loaded filter when filter signal changes before the next load completes', async () => {
    // Simulate the race: user navigates back, ngOnInit resets period synchronously,
    // but pagedResult still shows the previous load's data.
    apiSpy.list.and.returnValue(of(makePaged([makeItem('p1')])));
    store.setFilter({ periodFrom: '2026-01-01', periodTo: '2026-01-31' });
    await store.reload(); // _lastLoadedFilter = January

    // ngOnInit-equivalent: filter signal updated synchronously to this-month.
    // The new load has NOT completed yet — we don't call reload() again.
    store.setFilter({ periodFrom: '2026-06-01', periodTo: '2026-06-09' });

    // Export must use January (last loaded), NOT June (pending in-transition filter).
    const params = store.toExportParams();
    expect(params['periodFrom']).toBe('2026-01-01');
    expect(params['periodTo']).toBe('2026-01-31');
  });

  // ── Query param round-trip ────────────────────────────────────────────────

  it('toQueryParams / loadFromQueryParams round-trip preserves all fields', () => {
    const f: PayoutFilter = {
      payeeIds: ['id1', 'id2'],
      planIds: ['pid1'],
      status: 'Approved',
      currencies: ['EUR', 'PLN'],
      periodFrom: '2026-01-01',
      periodTo: '2026-03-31',
      paidFrom: '2026-07-01',
      paidTo: '2026-07-31',
      hideZero: true,
    };
    store.setFilter(f);

    const params = store.toQueryParams();
    store.clearFilters();
    store.loadFromQueryParams(params);

    const restored = store.filter();
    expect(restored.payeeIds).toEqual(['id1', 'id2']);
    expect(restored.planIds).toEqual(['pid1']);
    expect(restored.status).toBe('Approved');
    expect(restored.currencies).toEqual(['EUR', 'PLN']);
    expect(restored.periodFrom).toBe('2026-01-01');
    expect(restored.periodTo).toBe('2026-03-31');
    expect(restored.paidFrom).toBe('2026-07-01');
    expect(restored.paidTo).toBe('2026-07-31');
    expect(restored.hideZero).toBeTrue();
  });

  // The dashboard's cash-flow card deep-links with payFrom/payTo, and the list must forward them to the
  // API as paidFrom/paidTo — otherwise the list silently ignores the payment window and shows a total
  // that disagrees with the card the user just clicked.
  it('forwards a payment-date window to the API as paidFrom/paidTo', () => {
    store.setFilter({ paidFrom: '2026-07-01', paidTo: '2026-07-31' });

    const params = PayoutsStore._buildFilterRecord(store.filter());

    expect(params['paidFrom']).toBe('2026-07-01');
    expect(params['paidTo']).toBe('2026-07-31');
  });

  it('keeps the payment-date window separate from the compensation period', () => {
    store.setFilter({ periodFrom: '2026-01-01', periodTo: '2026-12-31' });

    const params = PayoutsStore._buildFilterRecord(store.filter());

    expect(params['periodFrom']).toBe('2026-01-01');
    expect(params['paidFrom']).toBeUndefined();
    expect(params['paidTo']).toBeUndefined();
  });

  it('toQueryParams / loadFromQueryParams round-trip preserves hideZero=false', () => {
    store.setFilter({ hideZero: false });

    const params = store.toQueryParams();
    expect(params['hz']).toBe('0');

    store.clearFilters();
    store.loadFromQueryParams(params);
    expect(store.filter().hideZero).toBeFalse();
  });

  // ── Error handling ────────────────────────────────────────────────────────

  it('error signal is set when API call fails', async () => {
    apiSpy.list.and.returnValue(throwError(() => new Error('Network error')));
    await store.reload();

    expect(store.error()).toBe('ERRORS.GENERIC');
  });
});

describe('PayoutsStore._buildFilterRecord', () => {
  it('emits excludeZero=true by default (hideZero is on by default)', () => {
    const record = PayoutsStore._buildFilterRecord(EMPTY_PAYOUT_FILTER);
    expect(record['excludeZero']).toBe('true');
    // Only excludeZero should be present for the empty filter
    expect(Object.keys(record)).toEqual(['excludeZero']);
  });

  it('omits excludeZero when hideZero is false', () => {
    const record = PayoutsStore._buildFilterRecord({ ...EMPTY_PAYOUT_FILTER, hideZero: false });
    expect(record['excludeZero']).toBeUndefined();
  });

  it('includes status when not All', () => {
    const record = PayoutsStore._buildFilterRecord({ ...EMPTY_PAYOUT_FILTER, status: 'Paid' });
    expect(record['status']).toBe('Paid');
  });

  it('joins arrays with commas', () => {
    const record = PayoutsStore._buildFilterRecord({
      ...EMPTY_PAYOUT_FILTER,
      payeeIds: ['a', 'b'],
      currencies: ['EUR'],
    });
    expect(record['payeeIds']).toBe('a,b');
    expect(record['currencies']).toBe('EUR');
  });
});
