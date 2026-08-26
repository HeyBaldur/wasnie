import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { of, throwError } from 'rxjs';
import { AssistantStore } from './state/assistant.store';
import { AssistantApiService } from './services/assistant.api.service';
import {
  AssistantConversation,
  AssistantMessage,
  AssistantStreamEvent,
} from './models/assistant.model';

/**
 * A stream belongs to the conversation that started it.
 *
 * ★ WHAT THESE TESTS ARE REALLY GUARDING. The visible symptom was cosmetic — ask in A, switch to B,
 * and B showed A's loader and A's Stop button, even when B was a brand new empty thread. The defect
 * underneath was not: every write went to "the conversation currently open", so the fragments, the
 * finished row and the failure of A's question all landed in whatever thread the user happened to be
 * reading when they arrived. In a product where an answer can name a payee's balance, that is one
 * conversation's content written into another.
 *
 * The rule these pin is one line long: the conversation is captured when the question is sent, and
 * every write for that turn goes there — never to whatever is on screen at the time.
 */

const CONVERSATION_A: AssistantConversation = {
  id: 'conv-a',
  title: 'Q3 planning',
  createdAt: '2026-08-26T09:00:00Z',
  updatedAt: '2026-08-26T09:00:00Z',
  messages: [],
  lastTurnUnanswered: false,
};

const CONVERSATION_B: AssistantConversation = {
  ...CONVERSATION_A,
  id: 'conv-b',
  title: 'Commission questions',
};

function userRow(content: string): AssistantMessage {
  return {
    id: `m-user-${content.length}`, role: 'User', content, payload: null, sequence: 0,
    createdAt: '2026-08-26T09:00:00Z',
  };
}

function assistantRow(content: string): AssistantMessage {
  return {
    id: `m-bot-${content.length}`, role: 'Assistant', content, payload: null, sequence: 1,
    createdAt: '2026-08-26T09:00:00Z',
  };
}

/** Lets the microtasks already queued reach the store. */
const settle = () => new Promise((resolve) => setTimeout(resolve, 0));

/**
 * A stream the test drives by hand: frames go in when the test says so, and it ends when the test
 * says so — or when the request is aborted, which is how a real one ends under Stop.
 *
 * ★ THE POINT IS THE PAUSE IN THE MIDDLE. Every scenario here is "the user did something WHILE the
 * answer was being written", so the stream has to be able to stay open across a conversation switch.
 */
class ManualStream {
  private readonly queue: AssistantStreamEvent[] = [];
  private wake: (() => void) | null = null;
  private ended = false;
  private aborted = false;
  private broken = false;

  push(frame: AssistantStreamEvent): void {
    this.queue.push(frame);
    this.wake?.();
  }

  end(): void {
    this.ended = true;
    this.wake?.();
  }

  /** The connection dies. The store cannot tell this from any other transport failure — nor should it. */
  fail(): void {
    this.broken = true;
    this.wake?.();
  }

  async *run(signal?: AbortSignal): AsyncGenerator<AssistantStreamEvent> {
    signal?.addEventListener('abort', () => {
      this.aborted = true;
      this.wake?.();
    });

    while (true) {
      while (this.queue.length > 0) {
        yield this.queue.shift()!;
      }

      if (this.aborted || signal?.aborted) {
        throw new DOMException('Aborted', 'AbortError');
      }

      if (this.broken) {
        throw new Error('network down');
      }

      if (this.ended) {
        return;
      }

      await new Promise<void>((resolve) => {
        this.wake = resolve;
      });
    }
  }
}

describe('AssistantStore — a stream belongs to the conversation that started it', () => {
  let store: AssistantStore;
  let api: jasmine.SpyObj<AssistantApiService>;
  /** The stream the store is handed, per conversation id, so a test can drive either one. */
  let streams: Map<string, ManualStream>;

  beforeEach(() => {
    streams = new Map();
    api = jasmine.createSpyObj<AssistantApiService>('AssistantApiService', [
      'getEntitlement', 'listConversations', 'getConversation', 'startConversation',
      'postMessage', 'streamMessage', 'renameConversation', 'deleteConversation',
    ]);
    api.getEntitlement.and.returnValue(of({ enabled: true, requiresUpgrade: false }));
    api.listConversations.and.returnValue(of([]));
    api.startConversation.and.returnValue(of(CONVERSATION_A));
    api.getConversation.and.callFake((id: string) =>
      of(id === CONVERSATION_B.id ? CONVERSATION_B : CONVERSATION_A));
    api.deleteConversation.and.returnValue(of(void 0));
    api.streamMessage.and.callFake(
      (id: string, _content: string, _token: string | null, signal?: AbortSignal) => {
        const stream = new ManualStream();
        streams.set(id, stream);
        return stream.run(signal);
      });

    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: AssistantApiService, useValue: api },
      ],
    });
    store = TestBed.inject(AssistantStore);
  });

  /**
   * Opens A and asks a question, leaving the answer half-written.
   *
   * ★ THE SEND IN FLIGHT COMES BACK WRAPPED, and it has to. Returning it bare from an async function
   * makes it a promise-of-a-promise, and `await` collapses those: the caller would sit waiting for the
   * very request this helper exists to leave hanging.
   */
  async function askInA(): Promise<{ sending: Promise<void> }> {
    await store.openConversation(CONVERSATION_A.id);
    const sending = store.send('what is my balance?');
    await settle();
    streams.get(CONVERSATION_A.id)!.push({ type: 'user', message: userRow('what is my balance?') });
    streams.get(CONVERSATION_A.id)!.push({ type: 'delta', delta: 'Your balance ' });
    await settle();
    return { sending };
  }

  it('★ switching away leaves the other conversation with no indicator at all', async () => {
    const { sending } = await askInA();
    expect(store.sending()).withContext('A is being answered').toBeTrue();

    await store.openConversation(CONVERSATION_B.id);

    expect(store.sending()).withContext('B was never asked anything').toBeFalse();
    expect(store.streamingReply()).withContext('no loader, no partial answer, in B').toBeNull();
    expect(store.progressSteps()).toEqual([]);

    streams.get(CONVERSATION_A.id)!.end();
    await sending;
  });

  it('★ a brand NEW conversation is clean while another one is still answering', async () => {
    const { sending } = await askInA();

    // The empty panel: no conversation open at all, which is the state the screenshot was taken in.
    store.conversation.set(null);

    expect(store.sending()).toBeFalse();
    expect(store.streamingReply()).toBeNull();

    streams.get(CONVERSATION_A.id)!.end();
    await sending;
  });

  it('★★ the fragments that arrive while B is open are written into A, never into B', async () => {
    const { sending } = await askInA();
    await store.openConversation(CONVERSATION_B.id);

    streams.get(CONVERSATION_A.id)!.push({ type: 'delta', delta: 'is 1,200.' });
    await settle();

    // The conversation on screen is untouched: not one token of A's answer reached it.
    expect(store.conversation()!.id).toBe(CONVERSATION_B.id);
    expect(store.messages()).toEqual([]);
    expect(store.streamingReply()).toBeNull();

    // And back in A, both halves are there — the one written before the switch and the one written
    // while another thread was on screen.
    await store.openConversation(CONVERSATION_A.id);
    expect(store.streamingReply()).toBe('Your balance is 1,200.');

    streams.get(CONVERSATION_A.id)!.end();
    await sending;
  });

  it('★★ the FINISHED row lands in the conversation that asked, not the one being read', async () => {
    const { sending } = await askInA();
    await store.openConversation(CONVERSATION_B.id);

    streams.get(CONVERSATION_A.id)!.push(
      { type: 'done', message: assistantRow('Your balance is 1,200.') });
    streams.get(CONVERSATION_A.id)!.end();
    await sending;

    // ★ THE ROW THIS TEST IS ABOUT IS THE ONE THAT IS NOT THERE. Before the fix, an answer about one
    // payee's balance was appended to whichever thread was open — this assertion is the whole WI.
    expect(store.conversation()!.id).toBe(CONVERSATION_B.id);
    expect(store.messages().map((m) => m.content))
      .withContext("B never asked anything, so B holds nothing").toEqual([]);
  });

  it('the answer is complete on returning, because the server is what holds it', async () => {
    const { sending } = await askInA();
    await store.openConversation(CONVERSATION_B.id);

    streams.get(CONVERSATION_A.id)!.push(
      { type: 'done', message: assistantRow('Your balance is 1,200.') });
    streams.get(CONVERSATION_A.id)!.end();
    await sending;

    // Coming back re-reads the thread: the row was persisted against the id in the request URL, so it
    // is there whether or not this browser was looking.
    api.getConversation.and.returnValue(of({
      ...CONVERSATION_A,
      messages: [userRow('what is my balance?'), assistantRow('Your balance is 1,200.')],
    }));
    await store.openConversation(CONVERSATION_A.id);

    expect(store.messages().map((m) => m.content))
      .toEqual(['what is my balance?', 'Your balance is 1,200.']);
    expect(store.sending()).withContext('the turn is over').toBeFalse();
  });

  it('★ two conversations can be answered at the same time without touching each other', async () => {
    const { sending: sendingA } = await askInA();

    await store.openConversation(CONVERSATION_B.id);
    const sendingB = store.send('and my commission?');
    await settle();
    streams.get(CONVERSATION_B.id)!.push({ type: 'delta', delta: 'Your commission ' });
    await settle();

    expect(store.sending()).withContext('B has its own turn in flight').toBeTrue();
    expect(store.streamingReply()).toBe('Your commission ');

    // A kept growing all along, in its own slot.
    streams.get(CONVERSATION_A.id)!.push({ type: 'delta', delta: 'is 1,200.' });
    await settle();
    expect(store.streamingReply())
      .withContext("B's bubble shows B's answer only").toBe('Your commission ');

    await store.openConversation(CONVERSATION_A.id);
    expect(store.streamingReply()).toBe('Your balance is 1,200.');

    streams.get(CONVERSATION_A.id)!.end();
    streams.get(CONVERSATION_B.id)!.end();
    await Promise.all([sendingA, sendingB]);
  });

  it('★ Stop ends the answer on screen and leaves the other one being written', async () => {
    const { sending: sendingA } = await askInA();

    await store.openConversation(CONVERSATION_B.id);
    const sendingB = store.send('and my commission?');
    await settle();
    streams.get(CONVERSATION_B.id)!.push({ type: 'delta', delta: 'Your commission ' });
    await settle();

    await store.cancel();
    await settle();

    expect(store.sending()).withContext('B was stopped').toBeFalse();

    await store.openConversation(CONVERSATION_A.id);
    expect(store.sending()).withContext('A never asked to be stopped').toBeTrue();
    expect(store.streamingReply()).toBe('Your balance ');

    streams.get(CONVERSATION_A.id)!.end();
    await Promise.all([sendingA, sendingB]);
  });

  it('★ deleting a conversation mid-answer aborts it and leaves nothing behind', async () => {
    const { sending } = await askInA();
    await store.openConversation(CONVERSATION_B.id);

    await store.remove(CONVERSATION_A.id);
    await sending;
    await settle();

    // The request was cut, and B — the thread on screen — was never told a failure happened.
    expect(store.errorKey()).toBeNull();
    expect(store.sending()).toBeFalse();

    // Nothing was left filed under the dead conversation either: reopening it shows a clean panel.
    api.getConversation.and.returnValue(of(CONVERSATION_A));
    await store.openConversation(CONVERSATION_A.id);
    expect(store.sending()).withContext('no orphan entry claiming to be busy').toBeFalse();
    expect(store.streamingReply()).toBeNull();
    expect(store.errorKey()).toBeNull();
  });

  it('★ a NEW conversation migrates onto its real id, so the answer is not orphaned', async () => {
    // Nothing open: the first question is what creates the thread.
    const sending = store.send('what is my balance?');
    await settle();

    expect(store.conversation()!.id).toBe(CONVERSATION_A.id);
    // The turn started under the new-conversation slot and moved with the id, exactly like the draft.
    expect(store.sending()).withContext('the state followed the thread').toBeTrue();

    streams.get(CONVERSATION_A.id)!.push({ type: 'delta', delta: 'Your balance is 1,200.' });
    await settle();
    expect(store.streamingReply()).toBe('Your balance is 1,200.');

    streams.get(CONVERSATION_A.id)!.push(
      { type: 'done', message: assistantRow('Your balance is 1,200.') });
    streams.get(CONVERSATION_A.id)!.end();
    await sending;

    expect(store.messages().map((m) => m.content)).toEqual(['Your balance is 1,200.']);
    expect(store.sending()).toBeFalse();
  });

  it('★ a failure is reported to the conversation that suffered it, not the one on screen', async () => {
    const { sending } = await askInA();
    await store.openConversation(CONVERSATION_B.id);

    streams.get(CONVERSATION_A.id)!.push(
      { type: 'error', errorKey: 'ASSISTANT.ERROR_RATE_LIMITED' });
    streams.get(CONVERSATION_A.id)!.end();
    await sending;

    expect(store.errorKey()).withContext('B did nothing wrong and is told nothing').toBeNull();
    expect(store.conversation()!.lastTurnUnanswered).toBeFalse();

    await store.openConversation(CONVERSATION_A.id);
    expect(store.errorKey()).toBe('ASSISTANT.ERROR_RATE_LIMITED');
  });

  it('★ words that never reached the server go back into THEIR OWN composer', async () => {
    await store.openConversation(CONVERSATION_A.id);
    const sending = store.send('a question that never landed');
    // The composer empties its own box on send; this stands in for that.
    store.clearDraft(CONVERSATION_A.id);
    await settle();

    // Nothing came back before the user moved on — so the server never stored the question either.
    await store.openConversation(CONVERSATION_B.id);
    streams.get(CONVERSATION_A.id)!.fail();
    await sending;

    // B's composer is untouched — the words are not B's.
    expect(store.activeDraft()).toBe('');
    expect(store.unsentText()).toBeNull();

    await store.openConversation(CONVERSATION_A.id);
    expect(store.activeDraft())
      .withContext('the lost question is waiting where it was typed')
      .toBe('a question that never landed');
    expect(store.unsentText()).toBe('a question that never landed');
  });

  it('does not overwrite something newer in that conversation\'s composer', async () => {
    await store.openConversation(CONVERSATION_A.id);
    const sending = store.send('a question that never landed');
    store.clearDraft(CONVERSATION_A.id);
    await settle();

    await store.openConversation(CONVERSATION_B.id);
    // The user went back to A and started typing something else before the failure unwound.
    store.setDraft(CONVERSATION_A.id, 'something else entirely');
    streams.get(CONVERSATION_A.id)!.fail();
    await sending;

    await store.openConversation(CONVERSATION_A.id);
    expect(store.activeDraft())
      .withContext('text the user typed is theirs').toBe('something else entirely');
  });

  it('a load failure of the OTHER conversation does not disturb the one being answered', async () => {
    const { sending } = await askInA();

    api.getConversation.and.returnValue(throwError(() => new Error('boom')));
    await store.openConversation(CONVERSATION_B.id);

    // The failed open leaves A on screen; its turn is untouched by the attempt.
    expect(store.sending()).toBeTrue();
    expect(store.streamingReply()).toBe('Your balance ');

    streams.get(CONVERSATION_A.id)!.end();
    await sending;
  });
});
