import { TestBed } from '@angular/core/testing';
import { of, Subject } from 'rxjs';

import { AssignmentsStore } from './assignments.store';
import { AssignmentsApiService } from '../services/assignments.api.service';
import { PagedResult } from '../../../shared/models/pagination.models';
import { Assignment } from '../models/assignment.model';

const makeAssignment = (id: string): Assignment => ({
  id,
  tenantId: 't1',
  planId: 'plan1',
  planName: 'Plan 1',
  planVersion: 1,
  payeeId: 'payee1',
  payeeFullName: 'Anna',
  payeeEmployeeCode: 'EMP001',
  effectiveStart: '2026-01-01',
  effectiveEnd: '2026-12-31',
  status: 'Active',
  notes: null,
} as Assignment);

const makePaged = (items: Assignment[]): PagedResult<Assignment> => ({
  items,
  totalCount: items.length,
  page: 1,
  pageSize: 10,
  totalPages: 1,
  hasNextPage: false,
  hasPreviousPage: false,
} as PagedResult<Assignment>);

describe('AssignmentsStore — URL filter contract (WI-2)', () => {
  let store: AssignmentsStore;
  let apiSpy: jasmine.SpyObj<AssignmentsApiService>;

  beforeEach(() => {
    apiSpy = jasmine.createSpyObj('AssignmentsApiService', ['getAssignments']);
    apiSpy.getAssignments.and.returnValue(of(makePaged([])));

    TestBed.configureTestingModule({
      providers: [
        AssignmentsStore,
        { provide: AssignmentsApiService, useValue: apiSpy },
      ],
    });
    store = TestBed.inject(AssignmentsStore);
  });

  describe('loadFromQueryParams is authoritative, not additive', () => {
    it('applies both params from the URL', () => {
      store.loadFromQueryParams({ payeeId: 'p1', status: 'Active' });

      expect(store.payeeId()).toBe('p1');
      expect(store.status()).toBe('Active');
    });

    // The reason this had to change in WI-2: the handler now runs on EVERY query-param change, so an
    // additive read would accumulate. Navigating from ?payeeId=X&status=Active to ?payeeId=Y would
    // silently carry the status along and show a narrower list than the URL promises.
    it('drops a param that the new URL no longer carries', () => {
      store.loadFromQueryParams({ payeeId: 'p1', status: 'Active' });
      store.loadFromQueryParams({ payeeId: 'p2' });

      expect(store.payeeId()).toBe('p2');
      expect(store.status()).toBeNull();
    });

    it('clears both when the URL carries neither', () => {
      store.loadFromQueryParams({ payeeId: 'p1', status: 'Active' });
      store.loadFromQueryParams({});

      expect(store.payeeId()).toBeNull();
      expect(store.status()).toBeNull();
    });

    it('resets to page 1 so a deep-link never lands on a page that no longer exists', () => {
      store.setPage(4);
      store.loadFromQueryParams({ payeeId: 'p1' });

      expect(store.page()).toBe(1);
    });
  });

  describe('clearUrlFilters — the screen default', () => {
    it('drops the URL-backed filters', () => {
      store.loadFromQueryParams({ payeeId: 'p1', status: 'Active' });
      store.clearUrlFilters();

      expect(store.payeeId()).toBeNull();
      expect(store.status()).toBeNull();
    });

    // `search` is the user's own typing and is NOT carried in the URL, so the URL has no business
    // clearing it. This is why the reset callback is per-screen instead of a blanket clearFilters().
    it('leaves the search box alone', () => {
      store.setSearch('anna');
      store.clearUrlFilters();

      expect(store.listParams().search).toBe('anna');
    });
  });

  describe('stale-response guard', () => {
    it('discards an older response that arrives after a newer one', async () => {
      const wide = new Subject<PagedResult<Assignment>>();
      const narrow = new Subject<PagedResult<Assignment>>();
      apiSpy.getAssignments.and.returnValues(wide.asObservable(), narrow.asObservable());

      const wideLoad = store.loadAssignments();
      const narrowLoad = store.loadAssignments();

      narrow.next(makePaged([makeAssignment('narrow')]));
      narrow.complete();
      await narrowLoad;

      wide.next(makePaged([makeAssignment('a'), makeAssignment('b')]));
      wide.complete();
      await wideLoad;

      expect(store.assignments().map(a => a.id)).toEqual(['narrow']);
    });
  });
});
