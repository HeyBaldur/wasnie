import { TestBed, fakeAsync, tick } from '@angular/core/testing';
import { of } from 'rxjs';
import { PayeesStore } from './payees.store';
import { PayeesApiService } from '../services/payees.api.service';
import { PagedResult } from '../../../shared/models/pagination.models';
import { Payee } from '../models/payee.model';

/**
 * REPRODUCTION — on the Payees list, searching for a payee and then CLEARING the box left the list
 * showing only that payee; and leaving the page and coming back left the same single row on screen
 * under an empty search box.
 *
 * The store is `providedIn: 'root'`, so whatever `search` holds outlives the component that set it.
 */

function page(names: string[]): PagedResult<Payee> {
  return {
    items: names.map(fullName => ({ fullName }) as Payee),
    totalCount: names.length,
    page: 1,
    pageSize: 25,
    totalPages: 1,
    hasNextPage: false,
    hasPreviousPage: false,
    unfilteredTotal: undefined,
  };
}

describe('PayeesStore — clearing the search', () => {
  let store: PayeesStore;
  let api: jasmine.SpyObj<PayeesApiService>;
  /** The `search` value of every list call the store made, in order. */
  let searches: string[];

  beforeEach(() => {
    searches = [];
    api = jasmine.createSpyObj<PayeesApiService>('PayeesApiService', ['getPayees']);
    api.getPayees.and.callFake((params?: { search?: string }) => {
      searches.push(params?.search ?? '');
      const s = params?.search ?? '';
      return of(s ? page([s]) : page(['Ada', 'Grace', 'Rudolph'])) as never;
    });

    TestBed.configureTestingModule({
      providers: [{ provide: PayeesApiService, useValue: api }],
    });
    store = TestBed.inject(PayeesStore);
  });

  it('clearing the box asks the server for the UNFILTERED list again', fakeAsync(() => {
    TestBed.flushEffects();
    tick(300);

    store.setSearch('Rudolph');
    TestBed.flushEffects();
    tick(300);
    TestBed.flushEffects();
    tick();
    expect(store.payees().map(p => p.fullName)).toEqual(['Rudolph'], 'precondition: the search applied');

    store.setSearch('');
    TestBed.flushEffects();
    tick(300);
    TestBed.flushEffects();
    tick();

    expect(searches[searches.length - 1]).toBe('', 'the last request must carry no search term');
    expect(store.payees().map(p => p.fullName)).toEqual(['Ada', 'Grace', 'Rudolph']);
  }));

  it('the search term does not survive leaving the screen', fakeAsync(() => {
    // The component is recreated on re-entry and its input renders empty, so a term still held here
    // puts a filtered list under a box that says nothing is filtered.
    TestBed.flushEffects();
    tick(300);

    store.setSearch('Rudolph');
    TestBed.flushEffects();
    tick(300);
    TestBed.flushEffects();
    tick();

    store.resetForEntry();
    TestBed.flushEffects();
    tick(300);
    TestBed.flushEffects();
    tick();

    expect(store.listParams().search).toBe('');
    expect(store.payees().map(p => p.fullName)).toEqual(['Ada', 'Grace', 'Rudolph']);
  }));

  it('a term set before leaving is gone the next time the screen is entered', fakeAsync(() => {
    // ★ THE USER-VISIBLE CONTRACT. The search box is recreated empty on every entry, so anything the
    //   store still holds is a filter with no visible cause and no way to clear it.
    TestBed.flushEffects();
    tick(300);

    store.setSearch('Rudolph');
    TestBed.flushEffects();
    tick(300);
    TestBed.flushEffects();
    tick();
    searches.length = 0;

    store.resetForEntry();          // what ngOnInit does on re-entry
    TestBed.flushEffects();
    tick(300);
    TestBed.flushEffects();
    tick();

    expect(searches).toContain('');
    expect(store.payees().length).toBeGreaterThan(1, 'the list is no longer narrowed to one person');
  }));

  it('entering with nothing to reset does not fire a needless reload', fakeAsync(() => {
    TestBed.flushEffects();
    tick(300);
    searches.length = 0;

    store.resetForEntry();
    TestBed.flushEffects();
    tick(300);
    TestBed.flushEffects();
    tick();

    expect(searches).toEqual([], 'a no-op reset must not cost a round trip on every entry');
  }));

  it('listParams reflects the term the list is actually filtered by', fakeAsync(() => {
    // The empty state reads this. If it lags behind, the screen offers "create your first payee" over
    // a list that is merely filtered.
    TestBed.flushEffects();
    tick(300);

    store.setSearch('Rudolph');
    TestBed.flushEffects();
    tick(300);
    TestBed.flushEffects();
    tick();

    expect(store.listParams().search).toBe('Rudolph');
  }));
});
