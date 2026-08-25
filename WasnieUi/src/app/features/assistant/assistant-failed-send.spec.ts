/**
 * The turn that fails, and what happens to the words the user typed.
 *
 * ★ THE BUG THESE PIN, IN ONE PICTURE. A user asked a second question, the send failed before the
 * server stored it, they pressed "Try again" — and their message was gone while a SECOND assistant
 * reply appeared under the first one. Two answers in a row, no question between them. Reproduced
 * against the real store before anything was changed:
 *
 *     after retry: [User:FIRST QUESTION | Assistant:FIRST ANSWER | Assistant:SECOND ANSWER]
 *     sent to the server: [{"SECOND QUESTION", isRetry:false}, {"FIRST QUESTION", isRetry:true}]
 *
 * Nothing deleted the message. `markThreadUnanswered()` ran even though NOTHING had been stored, which
 * made `lastTurnUnanswered` claim the thread ended on an open question; `retryable` believed it, went
 * looking for the last stored User turn, and found the PREVIOUS, already-answered one.
 */
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { of } from 'rxjs';

import { AssistantStore } from './state/assistant.store';
import { AssistantApiService } from './services/assistant.api.service';
import { AuthService } from '../../core/services/auth.service';
import { AssistantConversation, AssistantMessage, AssistantStreamEvent } from './models/assistant.model';

function msg(id: string, role: 'User' | 'Assistant', content: string, sequence: number): AssistantMessage {
  return { id, role, content, payload: null, sequence, createdAt: '' } as unknown as AssistantMessage;
}

/** A thread with one ANSWERED turn already in it — the shape that made the bug visible. */
function answeredThread(): AssistantConversation {
  return {
    id: 'c1',
    title: 'Thread',
    createdAt: '',
    updatedAt: '',
    lastTurnUnanswered: false,
    messages: [
      msg('u1', 'User', 'FIRST QUESTION', 0),
      msg('a1', 'Assistant', 'FIRST ANSWER', 1),
    ],
  } as AssistantConversation;
}

describe('A send that fails — the user\'s words are not lost', () => {
  let store: AssistantStore;
  let batches: AssistantStreamEvent[][];
  let sent: { content: string; isRetry: boolean }[];
  let throwOnFirst: boolean;

  function roles(): string[] {
    return (store.conversation()?.messages ?? []).map(m => `${m.role}:${m.content}`);
  }

  beforeEach(() => {
    batches = [];
    sent = [];
    throwOnFirst = false;
    let call = 0;

    const api = {
      startConversation: () => of(answeredThread()),
      listConversations: () => of([]),
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      streamMessage: async function* (_id: string, content: string, _t: any, _s: any, isRetry: boolean) {
        sent.push({ content, isRetry });
        const index = call++;
        if (index === 0 && throwOnFirst) {
          throw new Error('connection died');
        }
        for (const frame of batches[index] ?? []) {
          yield frame;
        }
      },
    };

    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: AssistantApiService, useValue: api },
        { provide: AuthService, useValue: { getAccessToken: () => 'token' } },
      ],
    });

    store = TestBed.inject(AssistantStore);
    store.conversation.set(answeredThread());
  });

  // ── The failure that arrives BEFORE the server stored anything ────────────

  describe('when the server never stored the question', () => {
    beforeEach(() => {
      batches = [
        [{ type: 'error', errorKey: 'ASSISTANT.ERROR_UNAVAILABLE' }],
        // ★ AND THE RETRY SUCCEEDS THE WAY THE REAL SERVER DOES. A send that is NOT a retry always
        // emits the `user` frame first — that frame is how a question enters the thread at all. An
        // earlier draft of this fixture omitted it, and the suite went red for a gap the product had
        // not created: the stub was describing a server that cannot exist.
        [
          { type: 'user', message: msg('u2', 'User', 'SECOND QUESTION', 2) },
          { type: 'done', message: msg('a2', 'Assistant', 'SECOND ANSWER', 3) },
        ],
      ];
    });

    // ★ The flag used to be set unconditionally, and that single lie is what the retry believed.
    it('★ does not claim the thread ends on an unanswered question', async () => {
      await store.send('SECOND QUESTION');

      expect(store.conversation()?.lastTurnUnanswered)
        .withContext('nothing was stored, so the thread ends where it already ended').toBeFalse();
    });

    // ★ The heart of it: the offer must be the user's OWN message, not the previous question.
    it('★ offers to retry the message that failed, not the previous one', async () => {
      await store.send('SECOND QUESTION');

      expect(store.retryable())
        .toEqual({ content: 'SECOND QUESTION', wasPersisted: false });
    });

    it('★ keeps the words, so the composer can hand them back', async () => {
      await store.send('SECOND QUESTION');

      expect(store.unsentText()).toBe('SECOND QUESTION');
    });

    it('★ retries the user\'s own message, as a fresh send', async () => {
      await store.send('SECOND QUESTION');
      await store.retry();

      expect(sent).toEqual([
        { content: 'SECOND QUESTION', isRetry: false },
        { content: 'SECOND QUESTION', isRetry: false },
      ]);
    });

    // ★ THE SCREENSHOT, AS AN ASSERTION. Two assistant replies with no question between them is a
    // structurally impossible conversation, and it is what the user was looking at.
    it('★ never leaves two assistant replies in a row', async () => {
      await store.send('SECOND QUESTION');
      await store.retry();

      const rows = roles();
      const adjacentAssistants = rows.some((row, i) =>
        i > 0 && row.startsWith('Assistant') && rows[i - 1].startsWith('Assistant'));

      expect(adjacentAssistants).withContext(rows.join(' | ')).toBeFalse();
    });

    // The user's turn still comes from the server on the retry — no local copy was ever invented.
    it('the retried question lands in the thread as the server\'s row', async () => {
      await store.send('SECOND QUESTION');
      await store.retry();

      expect(roles()).toEqual([
        'User:FIRST QUESTION',
        'Assistant:FIRST ANSWER',
        'User:SECOND QUESTION',
        'Assistant:SECOND ANSWER',
      ]);
    });

    // ★ NO OPTIMISTIC COPY — the decision this fix was built around. The thread must be untouched
    // while the words live only in the composer.
    it('★ does not put the unsent message into the thread', async () => {
      await store.send('SECOND QUESTION');

      expect(roles()).toEqual(['User:FIRST QUESTION', 'Assistant:FIRST ANSWER']);
    });
  });

  // ── The safety net, pinned on its own ────────────────────────────────────
  //
  // ★ THIS ONE IS NOT REDUNDANT, AND IT IS TESTED HERE PRECISELY BECAUSE IT LOOKS IT. With the flag
  // guarded, `lastTurnUnanswered` and an unsent message should never both be set by the same turn — so
  // reverting the ORDER of the two branches in `retryable` breaks nothing above, and the ordering reads
  // like dead defence somebody could tidy away.
  //
  // It is not dead: the two CAN legitimately disagree. The flag also arrives from the SERVER on a
  // reload, describing an older turn it really did store unanswered; a fresh send that dies before
  // reaching the server then adds an exact local record on top of it. Read flag-first, the retry would
  // re-answer the old stored question and drop the new message — the original bug, by a different
  // route. The exact record must win over the inference.

  describe('when the server reports an older unanswered turn AND a new send dies unsent', () => {
    beforeEach(() => {
      throwOnFirst = true;
      batches = [[], [
        { type: 'user', message: msg('u3', 'User', 'SECOND QUESTION', 3) },
        { type: 'done', message: msg('a3', 'Assistant', 'SECOND ANSWER', 4) },
      ]];
    });

    it('★ offers the message that just failed, not the one the server is still waiting on', async () => {
      await store.send('SECOND QUESTION');

      // The server's own view, as a reload would deliver it: an older question it stored and never
      // answered. Both records are now live and they name different messages.
      const current = store.conversation()!;
      store.conversation.set({
        ...current,
        lastTurnUnanswered: true,
        messages: [...current.messages, msg('u2', 'User', 'AN OLDER UNANSWERED QUESTION', 2)],
      });

      expect(store.retryable())
        .toEqual({ content: 'SECOND QUESTION', wasPersisted: false });
    });

    it('★ and retrying sends that message, not the older one', async () => {
      await store.send('SECOND QUESTION');

      const current = store.conversation()!;
      store.conversation.set({
        ...current,
        lastTurnUnanswered: true,
        messages: [...current.messages, msg('u2', 'User', 'AN OLDER UNANSWERED QUESTION', 2)],
      });

      await store.retry();

      expect(sent[1]).toEqual({ content: 'SECOND QUESTION', isRetry: false });
    });
  });

  // ── A transport failure, which is the likeliest way to die before the server sees anything ──

  describe('when the connection dies before the server sees it', () => {
    beforeEach(() => {
      throwOnFirst = true;
      batches = [
        [],
        [
          { type: 'user', message: msg('u2', 'User', 'SECOND QUESTION', 2) },
          { type: 'done', message: msg('a2', 'Assistant', 'SECOND ANSWER', 3) },
        ],
      ];
    });

    it('★ keeps the words and offers to retry them', async () => {
      await store.send('SECOND QUESTION');

      expect(store.unsentText()).toBe('SECOND QUESTION');
      expect(store.retryable()).toEqual({ content: 'SECOND QUESTION', wasPersisted: false });
      expect(store.conversation()?.lastTurnUnanswered).toBeFalse();
    });

    it('★ the retry sends the user\'s own message', async () => {
      await store.send('SECOND QUESTION');
      await store.retry();

      expect(sent[1]).toEqual({ content: 'SECOND QUESTION', isRetry: false });
      expect(roles()).toEqual([
        'User:FIRST QUESTION',
        'Assistant:FIRST ANSWER',
        'User:SECOND QUESTION',
        'Assistant:SECOND ANSWER',
      ]);
    });
  });

  // ── The path the design was written for, which must keep working ─────────

  describe('when the server DID store the question and the model then failed', () => {
    beforeEach(() => {
      batches = [
        [
          { type: 'user', message: msg('u2', 'User', 'SECOND QUESTION', 2) },
          { type: 'error', errorKey: 'ASSISTANT.ERROR_UNAVAILABLE' },
        ],
        [{ type: 'done', message: msg('a2', 'Assistant', 'SECOND ANSWER', 3) }],
      ];
    });

    it('keeps the user\'s turn in the thread — it is stored', async () => {
      await store.send('SECOND QUESTION');

      expect(roles()).toEqual([
        'User:FIRST QUESTION', 'Assistant:FIRST ANSWER', 'User:SECOND QUESTION',
      ]);
    });

    // ★ The flag is TRUE here, and must stay true: this is what a reload reconstructs the failure from.
    it('★ still marks the thread as ending on an unanswered question', async () => {
      await store.send('SECOND QUESTION');

      expect(store.conversation()?.lastTurnUnanswered).toBeTrue();
    });

    it('offers the stored question, and re-answers it without sending it again', async () => {
      await store.send('SECOND QUESTION');

      expect(store.retryable()).toEqual({ content: 'SECOND QUESTION', wasPersisted: true });

      await store.retry();

      expect(sent[1]).toEqual({ content: 'SECOND QUESTION', isRetry: true });
      // Not written twice: one question, one answer.
      expect(roles()).toEqual([
        'User:FIRST QUESTION',
        'Assistant:FIRST ANSWER',
        'User:SECOND QUESTION',
        'Assistant:SECOND ANSWER',
      ]);
    });

    it('has nothing for the composer to take back — the message is in the thread', async () => {
      await store.send('SECOND QUESTION');

      expect(store.unsentText()).toBeNull();
    });
  });
});
