import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { Subject, of, throwError } from 'rxjs';
import { AssistantStore } from './state/assistant.store';
import { AssistantApiService } from './services/assistant.api.service';
import { AssistantConversationPage, AssistantConversationSummary } from './models/assistant.model';

/**
 * Pinning, from the client's side.
 *
 * ★★ THE PINNED SET IS NOT A FLAG ON THE PAGED ROWS. The server EXCLUDES pinned conversations from the
 * cursor flow — otherwise the same row renders twice, once in its own group and once in its time band —
 * so the two lists are genuinely separate here, and every move is a move BETWEEN them. Restoring only
 * one of them after a failure would leave a duplicate or a hole, which is why the revert snapshots both.
 */

function summary(id: string, updatedAt: string): AssistantConversationSummary {
  return { id, title: id, createdAt: '', updatedAt, messageCount: 1 };
}

/** Newest first, which is the order the list is always in. */
const A = summary('a', '2026-08-26T12:00:00Z');
const B = summary('b', '2026-08-26T11:00:00Z');
const C = summary('c', '2026-08-26T10:00:00Z');

function page(
  items: AssistantConversationSummary[],
  pinned: AssistantConversationSummary[] = [],
  nextCursor: string | null = null,
): AssistantConversationPage {
  return { items, pinned, nextCursor };
}

describe('AssistantStore — pinning', () => {
  let store: AssistantStore;
  let api: jasmine.SpyObj<AssistantApiService>;

  beforeEach(() => {
    api = jasmine.createSpyObj<AssistantApiService>('AssistantApiService', [
      'getEntitlement', 'listConversations', 'getConversation', 'startConversation',
      'postMessage', 'streamMessage', 'renameConversation', 'deleteConversation',
      'pinConversation', 'unpinConversation',
    ]);
    api.getEntitlement.and.returnValue(of({ enabled: true, requiresUpgrade: false }));
    api.listConversations.and.returnValue(of(page([A, B, C])));
    api.pinConversation.and.returnValue(of(void 0));
    api.unpinConversation.and.returnValue(of(void 0));

    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: AssistantApiService, useValue: api },
      ],
    });
    store = TestBed.inject(AssistantStore);
  });

  // ══ The move ══════════════════════════════════════════════════════════════

  it('★ pinning moves the row OUT of the list and INTO the pinned group, without reloading', async () => {
    await store.loadConversations();
    api.listConversations.calls.reset();

    await store.pinConversation('b');

    expect(store.pinnedConversations().map((c) => c.id)).toEqual(['b']);
    expect(store.conversations().map((c) => c.id)).toEqual(['a', 'c'],
      'it must not stay behind, or it renders twice');
    expect(api.listConversations)
      .withContext('the move is local; refetching would throw away every loaded batch')
      .not.toHaveBeenCalled();
  });

  it('★ the row moves BEFORE the server answers', async () => {
    await store.loadConversations();

    const pending = new Subject<void>();
    api.pinConversation.and.returnValue(pending.asObservable());

    const pinning = store.pinConversation('b');

    // ★ THE POINT OF BEING OPTIMISTIC. Waiting for the round trip leaves the row sitting still long
    // enough to look broken, and people click again.
    expect(store.pinnedConversations().map((c) => c.id)).toEqual(['b']);
    expect(store.conversations().map((c) => c.id)).toEqual(['a', 'c']);

    pending.next();
    pending.complete();
    await pinning;
  });

  it('a newly pinned conversation goes to the TOP of the pinned group', async () => {
    api.listConversations.and.returnValue(of(page([B], [A])));
    await store.loadConversations();

    await store.pinConversation('b');

    expect(store.pinnedConversations().map((c) => c.id)).toEqual(['b', 'a'],
      'most recently pinned first — the same order the server sends');
  });

  it('pinning something already pinned does nothing at all', async () => {
    api.listConversations.and.returnValue(of(page([B], [A])));
    await store.loadConversations();

    await store.pinConversation('a');

    expect(api.pinConversation).not.toHaveBeenCalled();
    expect(store.pinnedConversations().map((c) => c.id)).toEqual(['a']);
  });

  // ══ Unpinning puts it back where it belongs ═══════════════════════════════

  it('★ unpinning puts the row back IN ORDER, not at the end', async () => {
    // ★ THE LIST IS SORTED BY LAST ACTIVITY AND THE GROUPING READS IT POSITIONALLY. Appending would file
    // a conversation from this morning under whatever band the last loaded row happens to be in.
    api.listConversations.and.returnValue(of(page([A, C], [B])));
    await store.loadConversations();

    await store.unpinConversation('b');

    expect(store.pinnedConversations()).toEqual([]);
    expect(store.conversations().map((c) => c.id)).toEqual(['a', 'b', 'c']);
  });

  it('★ unpinning something OLDER than everything loaded drops it rather than misplacing it', async () => {
    // ★ IT BELONGS IN A BATCH THAT HAS NOT BEEN FETCHED. Putting it at the end would show it above rows
    // that sort before it and would leave a duplicate behind when that batch finally arrives. The server
    // still holds it; "Load more" brings it back in its place.
    const ancient = summary('ancient', '2020-01-01T00:00:00Z');
    api.listConversations.and.returnValue(of(page([A, B], [ancient], 'CURSOR-1')));
    await store.loadConversations();

    await store.unpinConversation('ancient');

    expect(store.pinnedConversations()).toEqual([]);
    expect(store.conversations().map((c) => c.id)).toEqual(['a', 'b']);
  });

  // ══ ★★ The revert ════════════════════════════════════════════════════════

  it('★★ a failed pin puts BOTH lists back exactly as they were, and says so', async () => {
    await store.loadConversations();
    api.pinConversation.and.returnValue(throwError(() => ({ status: 500 })));

    await store.pinConversation('b');

    expect(store.pinnedConversations()).toEqual([]);
    expect(store.conversations().map((c) => c.id)).toEqual(['a', 'b', 'c'],
      'the row goes back where it was, in order');
    // ★ A SILENT REVERT IS WORSE THAN NO OPTIMISM: the list would flick and settle back with no
    // explanation, and the user would not know whether it worked.
    expect(store.listError()).toBe('PIN_FAILED');
  });

  it('★★ the CAP has its own sentence, taken from the server', async () => {
    // "Could not pin" for a limit the user can act on (unpin one) sends them hunting for a fault that is
    // not there. The server answers 422 with a translation key.
    await store.loadConversations();
    api.pinConversation.and.returnValue(
      throwError(() => ({ status: 422, error: { messageKey: 'ASSISTANT.PIN_LIMIT_REACHED' } })));

    await store.pinConversation('b');

    // ★ THE PREFIX IS STRIPPED so every value of listError is the same shape — the template prefixes
    // what it renders, and a qualified key would come out as ASSISTANT.ASSISTANT.… i.e. as nothing.
    expect(store.listError()).toBe('PIN_LIMIT_REACHED');
    expect(store.conversations().map((c) => c.id)).toEqual(['a', 'b', 'c']);
  });

  it('★ a failed unpin reverts too', async () => {
    api.listConversations.and.returnValue(of(page([A, C], [B])));
    await store.loadConversations();
    api.unpinConversation.and.returnValue(throwError(() => ({ status: 500 })));

    await store.unpinConversation('b');

    expect(store.pinnedConversations().map((c) => c.id)).toEqual(['b']);
    expect(store.conversations().map((c) => c.id)).toEqual(['a', 'c']);
    expect(store.listError()).toBe('UNPIN_FAILED');
  });

  // ══ The seam with paging and search ═══════════════════════════════════════

  it('★ Load more does not disturb the pinned group', async () => {
    api.listConversations.and.returnValue(of(page([A], [C], 'CURSOR-1')));
    await store.loadConversations();

    // A continuation carries no pinned group — the client must keep the one it has rather than
    // clearing it from an empty field.
    api.listConversations.and.returnValue(of(page([B], [], null)));
    await store.loadMoreConversations();

    expect(store.pinnedConversations().map((c) => c.id)).toEqual(['c']);
    expect(store.conversations().map((c) => c.id)).toEqual(['a', 'b']);
  });

  it('★ a search clears the pinned group, because searching has none', async () => {
    api.listConversations.and.returnValue(of(page([A], [C])));
    await store.loadConversations();
    expect(store.pinnedConversations()).not.toEqual([]);

    api.listConversations.and.returnValue(of(page([A], [])));
    store.searchTerm.set('anything');
    await store.loadConversations();

    expect(store.pinnedConversations()).toEqual([],
      'searching is a different mode and its results come back flat');
  });

  it('isPinned answers from the pinned group', async () => {
    api.listConversations.and.returnValue(of(page([A], [B])));
    await store.loadConversations();

    expect(store.isPinned('b')).toBeTrue();
    expect(store.isPinned('a')).toBeFalse();
  });
});
