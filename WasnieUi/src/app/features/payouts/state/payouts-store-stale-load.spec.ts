import { TestBed, fakeAsync, tick } from '@angular/core/testing';
import { Subject } from 'rxjs';
import { PayoutsStore, EMPTY_PAYOUT_FILTER } from './payouts.store';
import { PayoutsApiService } from '../services/payouts.api.service';
import { PagedResult } from '../../../shared/models/pagination.models';
import { PayoutListItem } from '../models/payout.model';

/**
 * REPRODUCTION — clicking the dashboard's "Approved — Not Paid" card opened /payouts?status=Approved
 * and showed the WHOLE list. Going back and clicking again showed the right rows.
 *
 * That asymmetry is the whole clue, and it points at the FIRST visit:
 *
 *   · First visit — the store does not exist yet. Injecting it schedules its constructor effect with
 *     the EMPTY filter (status "All"). The component's ngOnInit then applies the URL and sets the
 *     filter to Approved, which schedules the effect again. TWO loads go out: one unfiltered, one
 *     filtered. `_loadList` wrote `pagedResult` unconditionally, so whichever HTTP response arrived
 *     LAST won — and the unfiltered query is the bigger, slower one.
 *
 *   · Second visit — the store already holds `status: Approved`, so every load that entry triggers
 *     uses the same filter and the order stops mattering. Hence "I went back, clicked again, and now
 *     it appears".
 *
 * The fix is request sequencing in the store: a response that has been superseded is dropped instead
 * of overwriting a newer one.
 */

function page(ids: string[]): PagedResult<PayoutListItem> {
  return {
    items: ids.map(id => ({ id }) as PayoutListItem),
    totalCount: ids.length,
    page: 1,
    pageSize: 25,
    totalPages: 1,
    hasNextPage: false,
    hasPreviousPage: false,
    unfilteredTotal: undefined,
  };
}

describe('PayoutsStore — a superseded load must not overwrite a newer one', () => {
  let store: PayoutsStore;
  let inFlight: Subject<PagedResult<PayoutListItem>>[];

  beforeEach(() => {
    inFlight = [];
    const api = jasmine.createSpyObj<PayoutsApiService>('PayoutsApiService', ['list']);
    api.list.and.callFake(() => {
      const s = new Subject<PagedResult<PayoutListItem>>();
      inFlight.push(s);
      return s.asObservable();
    });

    TestBed.configureTestingModule({
      providers: [{ provide: PayoutsApiService, useValue: api }],
    });
    store = TestBed.inject(PayoutsStore);
  });

  /** Reproduces a first visit: the store is created, then the URL's filter is applied. */
  function enterPageWithUrlFilter(): void {
    TestBed.flushEffects();                                             // constructor effect — unfiltered
    store.setFilter({ ...EMPTY_PAYOUT_FILTER, status: 'Approved' });    // the URL lands
    TestBed.flushEffects();                                             // second load — filtered
  }

  it('a first visit really does start two loads, and the second is the filtered one', fakeAsync(() => {
    enterPageWithUrlFilter();

    expect(inFlight.length).toBe(2,
      'this is the condition that made the bug possible — if it ever drops to 1 the guard below is moot');
  }));

  it('the unfiltered answer arriving LAST does not replace the filtered rows', fakeAsync(() => {
    // THE PRODUCTION SYMPTOM, exactly: the whole list on screen under a URL that says status=Approved.
    enterPageWithUrlFilter();

    inFlight[1].next(page(['approved-1', 'approved-2']));   // filtered — answers first
    inFlight[1].complete();
    tick();
    inFlight[0].next(page(['a', 'b', 'c', 'd', 'e']));      // unfiltered — answers late
    inFlight[0].complete();
    tick();

    expect(store.items().map(i => i.id)).toEqual(['approved-1', 'approved-2']);
  }));

  it('the newest answer still wins when the two arrive in order', fakeAsync(() => {
    enterPageWithUrlFilter();

    inFlight[0].next(page(['a', 'b', 'c']));
    inFlight[0].complete();
    tick();
    inFlight[1].next(page(['approved-1']));
    inFlight[1].complete();
    tick();

    expect(store.items().map(i => i.id)).toEqual(['approved-1']);
  }));

  it('a superseded answer does not stop the spinner while the newer load is still running', fakeAsync(() => {
    enterPageWithUrlFilter();

    inFlight[0].next(page(['a']));
    inFlight[0].complete();
    tick();

    expect(store.loading())
      .withContext('a spinner that stops early says the screen is ready when it is not').toBeTrue();

    inFlight[1].next(page(['approved-1']));
    inFlight[1].complete();
    tick();

    expect(store.loading()).toBeFalse();
  }));

  it('a superseded answer does not become the filter that export uses', fakeAsync(() => {
    // toExportParams() reads the filter of the last COMPLETED load, so a stale completion would make
    // Export download the rows of a filter the screen is no longer showing.
    enterPageWithUrlFilter();

    inFlight[1].next(page(['approved-1']));
    inFlight[1].complete();
    tick();
    inFlight[0].next(page(['a', 'b']));
    inFlight[0].complete();
    tick();

    expect(store.toExportParams()['status']).toBe('Approved');
  }));

  it('an error from a superseded load is not shown over a newer successful one', fakeAsync(() => {
    enterPageWithUrlFilter();

    inFlight[1].next(page(['approved-1']));
    inFlight[1].complete();
    tick();
    inFlight[0].error(new Error('the unfiltered query timed out'));
    tick();

    expect(store.error())
      .withContext('the failure belongs to a request nobody is waiting for any more').toBeNull();
    expect(store.items().map(i => i.id)).toEqual(['approved-1']);
  }));
});
