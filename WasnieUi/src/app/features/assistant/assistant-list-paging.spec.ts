import { TestBed, fakeAsync, tick } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { Subject, of, throwError } from 'rxjs';
import { AssistantStore } from './state/assistant.store';
import { AssistantApiService } from './services/assistant.api.service';
import { AssistantConversationPage, AssistantConversationSummary } from './models/assistant.model';

/**
 * The history list, one batch at a time.
 *
 * ★ WHAT CHANGED AND WHY. The list used to arrive whole and the search box filtered it in the browser.
 * That was correct while everything was loaded — and it becomes a LIE the moment it is not: a filter
 * over the loaded batch answers "no results" while the match sits forty rows further down, unfetched.
 * The same class of untruth as telling somebody a record does not exist because the lookup could not
 * reach it. So the search moved to the server, and these pin the client half.
 */

function summary(id: string, title = id): AssistantConversationSummary {
  return { id, title, createdAt: '', updatedAt: '2026-08-26T09:00:00Z', messageCount: 1 };
}

function page(ids: string[], nextCursor: string | null): AssistantConversationPage {
  return { items: ids.map((id) => summary(id)), nextCursor, pinned: [] };
}

describe('AssistantStore — paging and searching the conversation list', () => {
  let store: AssistantStore;
  let api: jasmine.SpyObj<AssistantApiService>;

  beforeEach(() => {
    api = jasmine.createSpyObj<AssistantApiService>('AssistantApiService', [
      'getEntitlement', 'listConversations', 'getConversation', 'startConversation',
      'postMessage', 'streamMessage', 'renameConversation', 'deleteConversation',
    ]);
    api.getEntitlement.and.returnValue(of({ enabled: true, requiresUpgrade: false }));
    api.listConversations.and.returnValue(of(page([], null)));

    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: AssistantApiService, useValue: api },
      ],
    });
    store = TestBed.inject(AssistantStore);
  });

  // ══ Load more ═════════════════════════════════════════════════════════════

  it('the first batch replaces the list and remembers where to continue', async () => {
    api.listConversations.and.returnValue(of(page(['a', 'b'], 'CURSOR-1')));

    await store.loadConversations();

    expect(store.conversations().map((c) => c.id)).toEqual(['a', 'b']);
    expect(store.conversationsCursor()).toBe('CURSOR-1');
    expect(store.hasMoreConversations()).toBeTrue();
  });

  it('★ Load more APPENDS — it does not replace and does not duplicate', async () => {
    api.listConversations.and.returnValue(of(page(['a', 'b'], 'CURSOR-1')));
    await store.loadConversations();

    api.listConversations.and.returnValue(of(page(['c', 'd'], null)));
    await store.loadMoreConversations();

    expect(store.conversations().map((c) => c.id)).toEqual(['a', 'b', 'c', 'd']);
    expect(store.hasMoreConversations())
      .withContext('no cursor means no more, so the button disappears rather than sitting disabled')
      .toBeFalse();
  });

  it('★ a row that arrives twice is added once', async () => {
    // The cursor cannot return a row twice on its own — that is the property it exists for. But a
    // conversation the user ANSWERS between two clicks jumps to the top of the server's list while a
    // copy of it is already rendered here from an earlier batch. Filtering by id is cheap and makes a
    // duplicate row impossible regardless of what moved.
    api.listConversations.and.returnValue(of(page(['a', 'b'], 'CURSOR-1')));
    await store.loadConversations();

    api.listConversations.and.returnValue(of(page(['b', 'c'], null)));
    await store.loadMoreConversations();

    expect(store.conversations().map((c) => c.id)).toEqual(['a', 'b', 'c']);
  });

  it('Load more does nothing when there is no cursor', async () => {
    api.listConversations.and.returnValue(of(page(['a'], null)));
    await store.loadConversations();
    api.listConversations.calls.reset();

    await store.loadMoreConversations();

    expect(api.listConversations).not.toHaveBeenCalled();
  });

  it('the cursor is echoed back, never composed here', async () => {
    api.listConversations.and.returnValue(of(page(['a'], 'OPAQUE//CURSOR')));
    await store.loadConversations();

    api.listConversations.and.returnValue(of(page(['b'], null)));
    await store.loadMoreConversations();

    expect(api.listConversations.calls.mostRecent().args[0]).toBe('OPAQUE//CURSOR');
  });

  // ══ Search ════════════════════════════════════════════════════════════════

  it('★ a search goes to the SERVER, debounced, and reloads from the first batch', fakeAsync(() => {
    api.listConversations.and.returnValue(of(page(['a'], 'CURSOR-1')));
    void store.loadConversations();
    tick();
    api.listConversations.calls.reset();

    store.setSearch('comis');
    expect(api.listConversations).withContext('nothing goes out mid-word').not.toHaveBeenCalled();

    tick(300);

    expect(api.listConversations).toHaveBeenCalledTimes(1);
    // First batch of the SEARCH: no cursor, because the result set is a different list.
    expect(api.listConversations.calls.mostRecent().args).toEqual([null, 'comis']);
  }));

  it('★ typing quickly sends ONE request, for the final term', fakeAsync(() => {
    api.listConversations.calls.reset();

    store.setSearch('co');
    tick(100);
    store.setSearch('comi');
    tick(100);
    store.setSearch('comision');
    tick(300);

    expect(api.listConversations).toHaveBeenCalledTimes(1);
    expect(api.listConversations.calls.mostRecent().args[1]).toBe('comision');
  }));

  it('a term below the minimum is not searched for, and restores the ordinary list', fakeAsync(() => {
    api.listConversations.and.returnValue(of(page(['a', 'b'], null)));

    store.setSearch('comision');
    tick(300);
    expect(store.searchTerm()).toBe('comision');

    api.listConversations.calls.reset();
    store.setSearch('c');
    tick(300);

    expect(store.searchTerm()).toBe('', 'one character is "still typing", not a question');
    expect(api.listConversations.calls.mostRecent().args[1])
      .toBeNull('so the parameter is omitted and the ordinary list comes back');
  }));

  it('backspacing through a short word does not fire a request per key', fakeAsync(() => {
    api.listConversations.calls.reset();

    store.setSearch('ab');
    tick(300);
    const afterFirst = api.listConversations.calls.count();

    store.setSearch('a');
    tick(300);
    store.setSearch('');
    tick(300);

    expect(api.listConversations.calls.count()).toBe(afterFirst + 1,
      '"a" and "" are both "no search" — only the first transition is worth a round trip');
  }));

  it('clearing the search restores the ordinary list from the first batch', fakeAsync(() => {
    api.listConversations.and.returnValue(of(page(['found'], null)));
    store.setSearch('comision');
    tick(300);
    expect(store.conversations().map((c) => c.id)).toEqual(['found']);

    api.listConversations.and.returnValue(of(page(['a', 'b', 'c'], null)));
    store.setSearch('');
    tick(300);

    expect(store.searchTerm()).toBe('');
    expect(store.conversations().map((c) => c.id)).toEqual(['a', 'b', 'c']);
  }));

  it('Load more while searching keeps the term', fakeAsync(() => {
    api.listConversations.and.returnValue(of(page(['x'], 'CURSOR-1')));
    store.setSearch('comision');
    tick(300);

    api.listConversations.and.returnValue(of(page(['y'], null)));
    void store.loadMoreConversations();
    tick();

    expect(api.listConversations.calls.mostRecent().args).toEqual(['CURSOR-1', 'comision']);
  }));

  // ══ ★ Out-of-order responses ══════════════════════════════════════════════

  it('★★ a SLOW answer to an OLD term never overwrites a newer one', fakeAsync(() => {
    // The failure this prevents: typing "asignacion" fires a request for "asig" and one for
    // "asignacion". If the FIRST is slower its results land LAST, and the user reads matches for a word
    // they finished typing a second ago — with nothing on screen to tell them so.
    const slowOld = new Subject<AssistantConversationPage>();
    const fastNew = new Subject<AssistantConversationPage>();

    api.listConversations.and.returnValue(slowOld.asObservable());
    store.setSearch('asig');
    tick(300);

    api.listConversations.and.returnValue(fastNew.asObservable());
    store.setSearch('asignacion');
    tick(300);

    // The newer request answers first.
    fastNew.next(page(['right'], null));
    fastNew.complete();
    tick();

    expect(store.conversations().map((c) => c.id)).toEqual(['right']);

    // …and the older one finally arrives. It must be dropped on the floor.
    slowOld.next(page(['stale', 'stale2'], 'STALE-CURSOR'));
    slowOld.complete();
    tick();

    expect(store.conversations().map((c) => c.id)).toEqual(['right'],
      'the stale answer must not reach the screen');
    expect(store.conversationsCursor()).toBeNull(
      'nor may it leave a cursor pointing into a result set nobody is looking at');
  }));

  it('★ a stale FAILURE does not raise an error over a newer success', fakeAsync(() => {
    const slowOld = new Subject<AssistantConversationPage>();
    api.listConversations.and.returnValue(slowOld.asObservable());
    store.setSearch('asig');
    tick(300);

    api.listConversations.and.returnValue(of(page(['right'], null)));
    store.setSearch('asignacion');
    tick(300);

    slowOld.error(new Error('too late'));
    tick();

    expect(store.listError()).withContext('the request that failed is not the one on screen').toBeNull();
    expect(store.conversations().map((c) => c.id)).toEqual(['right']);
  }));

  // ══ ★ The blink ═══════════════════════════════════════════════════════════

  it('★★ OPENING a conversation does not put the RAIL into a loading state', async () => {
    // ★★ THE BLINK, REPORTED FROM RUNTIME. The list's loader was driven off the store's shared
    // `loading` signal — which `openConversation` also sets. So clicking a row in the sidebar blanked
    // the sidebar to "Loading…" and brought it back a moment later: a flash on every single change of
    // conversation, caused by a request the sidebar had no part in.
    //
    // Two things that can be true independently need two signals. This asserts the rail's signal stays
    // down while the chat pane's goes up.
    api.listConversations.and.returnValue(of(page(['a', 'b'], null)));
    await store.loadConversations();

    const pending = new Subject<never>();
    api.getConversation.and.returnValue(pending.asObservable());

    const opening = store.openConversation('a');

    expect(store.loading()).withContext('the CHAT PANE is loading').toBeTrue();
    expect(store.listLoading()).withContext('and the rail is not').toBeFalse();
    expect(store.conversations().map((c) => c.id))
      .withContext('the rows stay exactly where they were').toEqual(['a', 'b']);

    pending.complete();
    await opening;

    expect(store.listLoading()).toBeFalse();
  });

  it('★ a conversation that fails to OPEN does not blame the rail', async () => {
    // The other half of the same conflation: sharing `error` meant a failure opening a CHAT put "your
    // conversations could not be loaded" in the sidebar — a sentence about the rail, raised by
    // something the rail did not do.
    api.listConversations.and.returnValue(of(page(['a'], null)));
    await store.loadConversations();

    api.getConversation.and.returnValue(throwError(() => new Error('down')));
    await store.openConversation('a');

    expect(store.error()).toBe('LOAD_FAILED', 'the chat pane says so');
    expect(store.listError()).withContext('the rail stays quiet').toBeNull();
  });

  // ══ Failure is never silent ═══════════════════════════════════════════════

  it('★ a failed first batch is reported, not swallowed', async () => {
    api.listConversations.and.returnValue(throwError(() => new Error('down')));

    await store.loadConversations();

    expect(store.listError()).toBe('LOAD_FAILED');
    expect(store.listLoading()).toBeFalse();
  });

  it('★ a failed Load more is reported SEPARATELY, and keeps what is already on screen', async () => {
    api.listConversations.and.returnValue(of(page(['a', 'b'], 'CURSOR-1')));
    await store.loadConversations();

    api.listConversations.and.returnValue(throwError(() => new Error('down')));
    await store.loadMoreConversations();

    // ★ A LIST THAT SIMPLY STOPS READS AS "THIS IS ALL THERE IS" — a false statement about somebody's
    // own data that they have no way to check. The rows stay, the cursor stays, and the failure is said.
    expect(store.listError()).toBe('LOAD_MORE_FAILED');
    expect(store.conversations().map((c) => c.id)).toEqual(['a', 'b']);
    expect(store.conversationsCursor()).toBe('CURSOR-1', 'so Retry has somewhere to continue from');
    expect(store.loadingMore()).toBeFalse();
  });

  it('two Load more clicks in a row do not fire two requests', async () => {
    api.listConversations.and.returnValue(of(page(['a'], 'CURSOR-1')));
    await store.loadConversations();

    const pending = new Subject<AssistantConversationPage>();
    api.listConversations.and.returnValue(pending.asObservable());

    const first = store.loadMoreConversations();
    await store.loadMoreConversations();   // the impatient second click

    expect(api.listConversations.calls.count()).toBe(2, 'one first batch, one Load more — not three');

    pending.next(page(['b'], null));
    pending.complete();
    await first;
  });
});
