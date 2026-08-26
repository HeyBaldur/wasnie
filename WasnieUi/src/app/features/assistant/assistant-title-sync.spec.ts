import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { of } from 'rxjs';
import { AssistantStore } from './state/assistant.store';
import { AssistantApiService } from './services/assistant.api.service';
import {
  AssistantConversation,
  AssistantConversationSummary,
  AssistantMessage,
  AssistantStreamEvent,
} from './models/assistant.model';

/**
 * The header and the history list say the same thing about a conversation's name, at the same moment.
 *
 * ★★ THE DEFECT WAS NEVER THE TIMING OF THE TITLE. The server names a thread from the first thing said
 * in it, in the SAME write as the user's message and BEFORE the model is called — that was already
 * true. What was missing is that the client had no way to hear about it: the `user` frame carried the
 * message and nothing else, so the history list picked the name up later from its own refresh while the
 * open conversation's header held the object fetched before the send and went on saying "New
 * conversation". One fact, two arrival times, and the user watching both.
 */

const UNTITLED = '__UNTITLED__';

const CONVERSATION: AssistantConversation = {
  id: 'conv-1',
  title: UNTITLED,
  createdAt: '2026-08-26T09:00:00Z',
  updatedAt: '2026-08-26T09:00:00Z',
  messages: [],
  lastTurnUnanswered: false,
};

function userRow(content: string): AssistantMessage {
  return {
    id: 'm-user', role: 'User', content, payload: null, sequence: 0,
    createdAt: '2026-08-26T09:00:00Z',
  };
}

function assistantRow(content: string): AssistantMessage {
  return {
    id: 'm-bot', role: 'Assistant', content, payload: null, sequence: 1,
    createdAt: '2026-08-26T09:00:00Z',
  };
}

async function* frames(list: AssistantStreamEvent[]): AsyncGenerator<AssistantStreamEvent> {
  for (const frame of list) {
    yield frame;
  }
}

describe('AssistantStore — the title reaches the header and the list together', () => {
  let store: AssistantStore;
  let api: jasmine.SpyObj<AssistantApiService>;

  const TITLE = 'Is Incentra an EU product? Can…';

  /** What the list refresh reports, i.e. what the server holds. Tests move it as the server would. */
  let listTitle = UNTITLED;

  beforeEach(() => {
    listTitle = UNTITLED;
    api = jasmine.createSpyObj<AssistantApiService>('AssistantApiService', [
      'getEntitlement', 'listConversations', 'getConversation', 'startConversation',
      'postMessage', 'streamMessage', 'renameConversation', 'deleteConversation',
      'pinConversation', 'unpinConversation',
    ]);
    api.getEntitlement.and.returnValue(of({ enabled: true, requiresUpgrade: false }));
    api.startConversation.and.returnValue(of(CONVERSATION));
    api.getConversation.and.returnValue(of(CONVERSATION));

    // ★ THE REFRESH BEHAVES LIKE THE REAL SERVER: it returns whatever the title IS by the time it
    // runs, which after a send is the new one. An earlier draft of this file made it return the OLD
    // name to "prove" the frame did the work — and that is a scenario the server cannot produce, since
    // it wrote the title itself moments earlier. A test that only passes against an impossible backend
    // is not evidence. Which projection actually SOURCED the title is pinned by the empty-list test
    // below instead.
    //
    // ★ AND IT ECHOES WHAT THE STORE ALREADY HOLDS, with that title applied. Returning a fixed
    // one-row list instead would WIPE whatever each test seeded — including the pinned group, which
    // the refresh also replaces — and the failures would be the stub's, not the code's.
    const asServerSees = (list: readonly AssistantConversationSummary[]) =>
      list.map((c) => (c.id === 'conv-1' ? { ...c, title: listTitle } : c));

    api.listConversations.and.callFake(() => of({
      items: asServerSees(store.conversations()),
      nextCursor: null,
      pinned: asServerSees(store.pinnedConversations()),
    }));

    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: AssistantApiService, useValue: api },
      ],
    });
    store = TestBed.inject(AssistantStore);
  });

  /** A stream that stops after the user's turn — i.e. the model has not answered yet. */
  function streamUserTurnOnly(title: string | undefined, content = 'Is Incentra an EU product?'): void {
    // The server names the thread in the same write as the message, so from that instant BOTH the
    // frame and any later read report the same name. The stub keeps those in step.
    if (title) {
      listTitle = title;
    }

    api.streamMessage.and.callFake(() =>
      frames([{ type: 'user', message: userRow(content), title }]));
  }

  it('★★ the title is set by SENDING, without waiting for the answer', async () => {
    store.conversation.set(CONVERSATION);
    store.conversations.set([
      { id: 'conv-1', title: UNTITLED, createdAt: '', updatedAt: '', messageCount: 0 },
    ]);
    streamUserTurnOnly(TITLE);

    await store.send('Is Incentra an EU product?');

    // The stream carried no `done` frame — the assistant never answered — and the thread is named.
    expect(store.conversation()!.title).toBe(TITLE);
  });

  it('★★ the HEADER and the LIST show the same title after that send', async () => {
    // ★ THE REPORTED SCREENSHOT. The rail showed the new name while the header still read
    // "New conversation". Both projections are asserted in one test on purpose: passing one and not
    // the other is precisely the bug.
    store.conversation.set(CONVERSATION);
    store.conversations.set([
      { id: 'conv-1', title: UNTITLED, createdAt: '', updatedAt: '', messageCount: 0 },
    ]);
    streamUserTurnOnly(TITLE);

    await store.send('Is Incentra an EU product?');

    expect(store.conversation()!.title).toBe(TITLE, 'the header');
    expect(store.conversations()[0].title).toBe(TITLE, 'the list');
  });

  it('★ a PINNED row is renamed too', async () => {
    // A pinned conversation is not in the paged array at all — the server excludes it so it cannot
    // render twice — so updating only `conversations` would leave the group at the top showing the
    // old name.
    store.conversation.set(CONVERSATION);
    store.conversations.set([]);
    store.pinnedConversations.set([
      { id: 'conv-1', title: UNTITLED, createdAt: '', updatedAt: '', messageCount: 0 },
    ]);
    streamUserTurnOnly(TITLE);

    await store.send('Is Incentra an EU product?');

    expect(store.pinnedConversations()[0].title).toBe(TITLE);
  });

  it('★ a title arriving for a conversation the user LEFT still lands in that conversation', async () => {
    // The same rule the whole streaming map lives under: the turn belongs to the thread that asked,
    // whatever is on screen when the frame arrives.
    store.conversation.set(CONVERSATION);
    store.conversations.set([
      { id: 'conv-1', title: UNTITLED, createdAt: '', updatedAt: '', messageCount: 0 },
      { id: 'conv-2', title: 'Another thread', createdAt: '', updatedAt: '', messageCount: 0 },
    ]);
    streamUserTurnOnly(TITLE);

    const sending = store.send('Is Incentra an EU product?');
    store.conversation.set({ ...CONVERSATION, id: 'conv-2', title: 'Another thread' });
    await sending;

    expect(store.conversations()[0].title).toBe(TITLE, 'the row that asked');
    expect(store.conversations()[1].title).toBe('Another thread', 'and not the one being read');
    expect(store.conversation()!.title).toBe('Another thread');
  });

  // ══ It only names an unnamed thread ═══════════════════════════════════════

  it('★ a SECOND message does not re-title anything', async () => {
    // The server sends the title on every user frame — "what it is called now" is always true and
    // needs no branch. So the client must be content to write the same value again.
    store.conversation.set({ ...CONVERSATION, title: TITLE });
    store.conversations.set([
      { id: 'conv-1', title: TITLE, createdAt: '', updatedAt: '', messageCount: 2 },
    ]);
    streamUserTurnOnly(TITLE, 'a second question');

    await store.send('a second question');

    expect(store.conversation()!.title).toBe(TITLE);
    expect(store.conversations()[0].title).toBe(TITLE);
  });

  it('★★ a name the USER chose is never overwritten', async () => {
    // The server refuses to re-title a named thread (TitleFromFirstMessage only names an untitled
    // one), so what it sends back on the next turn is the user's own name — and this asserts the
    // client writes THAT rather than deriving anything of its own.
    const chosen = 'Legal questions for the board';
    store.conversation.set({ ...CONVERSATION, title: chosen });
    store.conversations.set([
      { id: 'conv-1', title: chosen, createdAt: '', updatedAt: '', messageCount: 2 },
    ]);
    streamUserTurnOnly(chosen, 'another question entirely');

    await store.send('another question entirely');

    expect(store.conversation()!.title).toBe(chosen);
    expect(store.conversations()[0].title).toBe(chosen);
  });

  it('★★ the HEADER is titled by the FRAME, not by the list refresh', async () => {
    // ★★ THIS IS THE ONE THAT LOCATES THE FIX. The list refresh cannot be the header's source here
    // because the conversation is not IN the list — a brand new thread often is not yet. If the header
    // still ends up named, the only thing that could have named it is the `user` frame.
    store.conversation.set(CONVERSATION);
    store.conversations.set([]);
    api.listConversations.and.returnValue(of({ items: [], nextCursor: null, pinned: [] }));
    api.streamMessage.and.callFake(() =>
      frames([{ type: 'user', message: userRow('Is Incentra an EU product?'), title: TITLE }]));

    await store.send('Is Incentra an EU product?');

    expect(store.conversation()!.title).toBe(TITLE);
  });

  it('a frame without a title changes nothing — an older backend keeps working', async () => {
    store.conversation.set(CONVERSATION);
    streamUserTurnOnly(undefined);

    await store.send('Is Incentra an EU product?');

    expect(store.conversation()!.title).toBe(UNTITLED);
  });

  // ══ The new conversation, and the failed send ═════════════════════════════

  it('★ a BRAND NEW conversation is titled on its first send', async () => {
    // Nothing open: the send creates the thread, and the title has to survive that transition — the
    // frame arrives keyed to the id the server just assigned.
    streamUserTurnOnly(TITLE);

    await store.send('Is Incentra an EU product?');

    expect(store.conversation()!.id).toBe('conv-1');
    expect(store.conversation()!.title).toBe(TITLE);
  });

  it('★★ a first send that never reaches the server leaves nothing titled', async () => {
    // ★ AND THIS NEEDED NO CODE. The server sets the title in the SAME SaveChanges as the message, so
    // a turn that was never persisted cannot have named anything — and the client only ever learns a
    // title from the `user` frame, which is emitted after that write. Asserted rather than assumed,
    // because "titled and empty" is exactly the state the WI was worried about.
    store.conversation.set(CONVERSATION);
    store.conversations.set([
      { id: 'conv-1', title: UNTITLED, createdAt: '', updatedAt: '', messageCount: 0 },
    ]);
    api.streamMessage.and.callFake(() =>
      frames([{ type: 'error', errorKey: 'ASSISTANT.ERROR_UNAVAILABLE' }]));

    await store.send('Is Incentra an EU product?');

    expect(store.conversation()!.title).toBe(UNTITLED);
    expect(store.conversations()[0].title).toBe(UNTITLED);
    expect(store.messages()).toEqual([]);
  });

  it('the title also survives a full exchange that does answer', async () => {
    store.conversation.set(CONVERSATION);
    api.streamMessage.and.callFake(() =>
      frames([
        { type: 'user', message: userRow('Is Incentra an EU product?'), title: TITLE },
        { type: 'delta', delta: 'It is ' },
        { type: 'done', message: assistantRow('It is a commission product.') },
      ]));

    await store.send('Is Incentra an EU product?');

    expect(store.conversation()!.title).toBe(TITLE);
    expect(store.messages().map((m) => m.id)).toEqual(['m-user', 'm-bot']);
  });
});
