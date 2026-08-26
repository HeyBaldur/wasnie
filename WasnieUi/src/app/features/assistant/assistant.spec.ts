import { ComponentFixture, TestBed, fakeAsync, tick } from '@angular/core/testing';
import { ApplicationRef } from '@angular/core';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { Router, provideRouter } from '@angular/router';
import { TranslateModule, TranslateService } from '@ngx-translate/core';
import { NEVER, of, throwError } from 'rxjs';
import { AssistantStore } from './state/assistant.store';
import { AssistantApiService } from './services/assistant.api.service';
import { By } from '@angular/platform-browser';
import { AssistantPanelComponent } from './panel/assistant-panel.component';
import { AssistantConversationComponent } from './conversation/assistant-conversation.component';
import { AssistantTriggerComponent } from './trigger/assistant-trigger.component';
import {
  ASSISTANT_NOT_CONNECTED,
  AssistantConversation,
  AssistantExchange,
  AssistantMessage,
  AssistantStreamEvent,
  isPlaceholderReply,
} from './models/assistant.model';

/**
 * The assistant panel with NO AI behind it.
 *
 * What these tests pin is that the shell works end to end — the panel opens, a message round-trips
 * through the server and comes back as two stored turns, a previous conversation reopens — and that
 * the entry point is HIDDEN, not disabled, for a user without the entitlement.
 */

const CONVERSATION: AssistantConversation = {
  id: 'conv-1',
  title: 'Q3 planning',
  createdAt: '2026-07-31T09:00:00Z',
  updatedAt: '2026-07-31T09:00:00Z',
  messages: [],
  lastTurnUnanswered: false,
};

function exchange(content: string): AssistantExchange {
  return {
    userMessage: {
      id: 'm-user', role: 'User', content, payload: null, sequence: 0,
      createdAt: '2026-07-31T09:00:00Z',
    },
    assistantMessage: {
      id: 'm-bot', role: 'Assistant', content: ASSISTANT_NOT_CONNECTED, payload: null, sequence: 1,
      createdAt: '2026-07-31T09:00:00Z',
    },
  };
}

function apiSpy(): jasmine.SpyObj<AssistantApiService> {
  const api = jasmine.createSpyObj<AssistantApiService>('AssistantApiService', [
    'getEntitlement', 'listConversations', 'getConversation', 'startConversation',
    'postMessage', 'streamMessage', 'renameConversation', 'deleteConversation',
  ]);
  api.getEntitlement.and.returnValue(of({ enabled: true, requiresUpgrade: false }));
  api.listConversations.and.returnValue(of({ items: [], nextCursor: null, pinned: [] }));
  api.startConversation.and.returnValue(of(CONVERSATION));
  api.getConversation.and.returnValue(of(CONVERSATION));
  api.postMessage.and.returnValue(of(exchange('hello')));
  api.deleteConversation.and.returnValue(of(void 0));
  // The panel streams; the fake replays the frames a connected model would produce.
  api.streamMessage.and.callFake((_id: string, content: string) =>
    frames([
      { type: 'user', message: exchange(content).userMessage },
      { type: 'delta', delta: 'A real ' },
      { type: 'delta', delta: 'answer.' },
      { type: 'done', message: { ...exchange(content).assistantMessage, content: 'A real answer.' } },
    ]));
  return api;
}

/** Turns a list of frames into the async generator the store consumes. */
async function* frames(list: AssistantStreamEvent[]): AsyncGenerator<AssistantStreamEvent> {
  for (const frame of list) {
    yield frame;
  }
}

describe('AssistantStore', () => {
  let store: AssistantStore;
  let api: jasmine.SpyObj<AssistantApiService>;

  beforeEach(() => {
    api = apiSpy();
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: AssistantApiService, useValue: api },
      ],
    });
    store = TestBed.inject(AssistantStore);
  });

  it('opens and closes the panel', async () => {
    expect(store.isOpen()).toBeFalse();

    await store.open();
    expect(store.isOpen()).toBeTrue();
    expect(api.listConversations).toHaveBeenCalled();

    store.close();
    expect(store.isOpen()).toBeFalse();
    expect(store.historyOpen()).toBeFalse();
  });

  it('appends the SERVER rows for both turns, not an optimistic local copy', async () => {
    await store.startConversation();
    await store.send('hello');

    // Two messages, in server order, with the ids the server assigned. Echoing what we typed instead
    // would create a second version of the message that can drift from the stored row.
    expect(store.messages().length).toBe(2);
    expect(store.messages()[0].id).toBe('m-user');
    expect(store.messages()[1].id).toBe('m-bot');
    expect(api.streamMessage).toHaveBeenCalled();
    expect(api.streamMessage.calls.mostRecent().args.slice(0, 2)).toEqual(['conv-1', 'hello']);
  });

  it('starts a conversation on the first message when there is none', async () => {
    // The empty panel must not force the user to press "new conversation" before they may talk.
    expect(store.hasConversation()).toBeFalse();

    await store.send('first words');

    expect(api.startConversation).toHaveBeenCalled();
    expect(store.hasConversation()).toBeTrue();
  });

  it('ignores an empty message and does not call the server', async () => {
    await store.send('   ');
    expect(api.startConversation).not.toHaveBeenCalled();
    expect(api.streamMessage).not.toHaveBeenCalled();
  });

  it('reopens a previous conversation with its messages', async () => {
    const previous: AssistantConversation = {
      ...CONVERSATION,
      id: 'conv-old',
      title: 'Last week',
      messages: [
        { id: 'a', role: 'User', content: 'older question', payload: null, sequence: 0, createdAt: '2026-07-20T09:00:00Z' },
        { id: 'b', role: 'Assistant', content: ASSISTANT_NOT_CONNECTED, payload: null, sequence: 1, createdAt: '2026-07-20T09:00:00Z' },
      ],
    };
    api.getConversation.and.returnValue(of(previous));

    await store.openConversation('conv-old');

    expect(api.getConversation).toHaveBeenCalledWith('conv-old');
    expect(store.conversation()?.title).toBe('Last week');
    expect(store.messages().map((m) => m.sequence)).toEqual([0, 1]);
  });

  it('★ renders the answer as it streams, then replaces it with the stored row', async () => {
    await store.startConversation();
    await store.send('hello');

    // The temporary bubble is gone once `done` arrived...
    expect(store.streamingReply()).toBeNull();
    // ...and what remains are the two PERSISTED rows, with the model's real answer.
    expect(store.messages().map((m) => m.content)).toEqual(['hello', 'A real answer.']);
    expect(store.messages()[1].content).not.toBe(ASSISTANT_NOT_CONNECTED);
  });

  it('★ discards the partial answer when the stream fails, and keeps the question', async () => {
    // ★ The server stored nothing for the assistant, so leaving the fragments on screen would show a
    // message the user cannot find again. The question they asked IS stored, so retrying is free.
    api.streamMessage.and.callFake((_id: string, content: string) =>
      frames([
        { type: 'user', message: exchange(content).userMessage },
        { type: 'delta', delta: 'The answer begins' },
        { type: 'error', errorKey: 'ASSISTANT.ERROR_RATE_LIMITED' },
      ]));

    await store.startConversation();
    await store.send('a question');

    expect(store.streamingReply()).toBeNull('the partial text must not linger');
    expect(store.errorKey()).toBe('ASSISTANT.ERROR_RATE_LIMITED');
    // The user's turn survived; no assistant row was added.
    expect(store.messages().map((m) => m.role)).toEqual(['User']);
  });

  it('surfaces a transport failure as the generic error key', async () => {
    api.streamMessage.and.throwError('network down');

    await store.startConversation();
    await store.send('a question');

    expect(store.errorKey()).toBe('ASSISTANT.ERROR_UNAVAILABLE');
    expect(store.streamingReply()).toBeNull();
  });

  it('clears a previous error when a new message is sent', async () => {
    // A stale error under a fresh answer would read as if the new one had failed too.
    store.setStreamState(store.activeDraftKey(), { errorKey: 'ASSISTANT.ERROR_UNAVAILABLE' });

    await store.startConversation();
    await store.send('trying again');

    expect(store.errorKey()).toBeNull();
  });

  // ── ★ The steps of a turn ──────────────────────────────────────────────────

  it('★ builds the step list from the frames the server sent, and ticks each one on its done', async () => {
    // ★ NOTHING IS PREDICTED. The list is exactly what arrived, in the order it arrived, and a step is
    // green because the server said so — not because the next one started or a timer expired.
    // ★ OBSERVED FROM INSIDE THE STREAM, not by racing microtasks from outside. A `for await` resumes
    // the generator only after it has handled the frame it was given, so the line below runs at a
    // precisely known moment: the search has been announced and nothing else has arrived yet. That is
    // the state the loader actually renders, and it is the only one worth asserting.
    let midTurn: { phase: string; done: boolean }[] = [];

    api.streamMessage.and.callFake((_id: string, content: string) =>
      (async function* () {
        yield { type: 'user', message: exchange(content).userMessage } as AssistantStreamEvent;
        yield { type: 'progress', phase: 'understanding', state: 'start' } as AssistantStreamEvent;
        yield { type: 'progress', phase: 'understanding', state: 'done' } as AssistantStreamEvent;
        yield { type: 'progress', phase: 'searching_data', state: 'start' } as AssistantStreamEvent;
        midTurn = store.progressSteps();
        yield { type: 'progress', phase: 'searching_data', state: 'done' } as AssistantStreamEvent;
        yield { type: 'delta', delta: 'Answer.' } as AssistantStreamEvent;
        yield {
          type: 'done',
          message: { ...exchange(content).assistantMessage, content: 'Answer.' },
        } as AssistantStreamEvent;
      })());

    await store.startConversation();
    await store.send('what happened with TERM-CC-10?');

    // One finished step and one still running — in the order the server announced them.
    expect(midTurn).toEqual([
      { phase: 'understanding', done: true },
      { phase: 'searching_data', done: false },
    ]);

    // The answer landed: the steps go with the loader rather than staying as a finished checklist.
    expect(store.progressSteps()).toEqual([]);
    expect(store.messages().map((m) => m.content)).toEqual(['what happened with TERM-CC-10?', 'Answer.']);
  });

  it('★ a stream with NO progress frames still answers — the events are additive', async () => {
    // The compatibility guarantee, from the client side: the default fake in this file sends `user`,
    // deltas and `done` and nothing else. Nothing may depend on a progress frame having arrived.
    await store.startConversation();
    await store.send('hello');

    expect(store.progressSteps()).toEqual([]);
    expect(store.messages().map((m) => m.content)).toEqual(['hello', 'A real answer.']);
    expect(store.errorKey()).toBeNull();
  });

  it('★ a failed turn clears the steps instead of leaving a half-ticked list above the error', async () => {
    // How far it got is not an outcome. Nothing was persisted, and the only thing to do is retry.
    api.streamMessage.and.callFake((_id: string, content: string) =>
      frames([
        { type: 'user', message: exchange(content).userMessage },
        { type: 'progress', phase: 'understanding', state: 'start' },
        { type: 'progress', phase: 'understanding', state: 'done' },
        { type: 'progress', phase: 'searching_data', state: 'start' },
        { type: 'error', errorKey: 'ASSISTANT.ERROR_UNAVAILABLE' },
      ]));

    await store.startConversation();
    await store.send('a question');

    expect(store.progressSteps()).toEqual([]);
    expect(store.errorKey()).toBe('ASSISTANT.ERROR_UNAVAILABLE');
  });

  it("clears the previous turn's steps when the next one starts", async () => {
    store.setStreamState(store.activeDraftKey(), { steps: [{ phase: 'searching_data', done: true }] });

    await store.startConversation();
    await store.send('a new question');

    expect(store.progressSteps()).toEqual([]);
  });

  it('treats a failed entitlement check as NO access, never as yes', async () => {
    // Guessing generously here would only render a button that 403s on first use.
    api.getEntitlement.and.returnValue(throwError(() => new Error('network')));

    await store.loadEntitlement();

    expect(store.entitled()).toBeFalse();
  });
});

describe('AssistantTriggerComponent — hide, do not disable', () => {
  let fixture: ComponentFixture<AssistantTriggerComponent>;
  let api: jasmine.SpyObj<AssistantApiService>;

  async function setup(enabled: boolean, requiresUpgrade = false) {
    api = apiSpy();
    api.getEntitlement.and.returnValue(of({ enabled, requiresUpgrade }));

    await TestBed.configureTestingModule({
      imports: [AssistantTriggerComponent, TranslateModule.forRoot()],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: AssistantApiService, useValue: api },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(AssistantTriggerComponent);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  it('renders the button for an entitled user', async () => {
    await setup(true);
    expect(fixture.nativeElement.querySelector('button')).toBeTruthy();
  });

  it('★ renders NOTHING for a user without the entitlement — not a disabled button', async () => {
    await setup(false);

    const button = fixture.nativeElement.querySelector('button');
    expect(button).toBeNull('a forbidden action is hidden, never shown disabled');
  });

  // ── The billing refusal is the ONE that gets shown ──────────────────────────────────────────────

  it('★ renders a LOCKED link to the plans when the only thing missing is a paid plan', async () => {
    // The seat is held; the workspace is on Free. Hiding here would hide something the user can buy,
    // so this refusal — and only this one — is visible and leads somewhere.
    await setup(false, true);

    expect(fixture.nativeElement.querySelector('button'))
      .toBeNull('still not a live trigger — the assistant cannot be opened');

    const link: HTMLAnchorElement = fixture.nativeElement.querySelector('a');
    expect(link).toBeTruthy('the locked entry point is rendered');
    expect(link.getAttribute('href')).toBe('/subscription', 'clicking it goes where the plan is bought');
  });

  it('shows nothing at all when there is no seat, whatever the plan says', async () => {
    // Belt and braces on the backend contract: an upsell must never be shown to someone a bigger plan
    // would not help. If these two flags ever disagree, the absence of a seat wins.
    await setup(false, false);

    expect(fixture.nativeElement.querySelector('button')).toBeNull();
    expect(fixture.nativeElement.querySelector('a')).toBeNull();
  });

  it('renders nothing while the entitlement is still unknown', async () => {
    // No flash of a button the user may not be allowed to use.
    api = apiSpy();
    await TestBed.configureTestingModule({
      imports: [AssistantTriggerComponent, TranslateModule.forRoot()],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: AssistantApiService, useValue: api },
      ],
    }).compileComponents();

    const f = TestBed.createComponent(AssistantTriggerComponent);
    const store = TestBed.inject(AssistantStore);
    store.entitled.set(null);
    f.detectChanges();

    expect(f.nativeElement.querySelector('button')).toBeNull();
  });
});

describe('AssistantPanelComponent — placeholder rendering', () => {
  let fixture: ComponentFixture<AssistantPanelComponent>;
  let store: AssistantStore;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [AssistantPanelComponent, TranslateModule.forRoot()],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        // ws-empty-state renders an optional RouterLink action, so the panel needs a router context.
        provideRouter([]),
        { provide: AssistantApiService, useValue: apiSpy() },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(AssistantPanelComponent);
    store = TestBed.inject(AssistantStore);
  });

  it('is not in the DOM while closed', () => {
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('[data-testid="assistant-panel"]')).toBeNull();
  });

  it('★ renders the assistant sentinel as translated copy, never as the raw marker', () => {
    // The backend stores a language-neutral marker so a Spanish and a Polish reader can each see the
    // placeholder in their own language. Printing the row verbatim would leak "__ASSISTANT_NOT_CONNECTED__"
    // into the UI — this is the test that keeps that from shipping.
    store.isOpen.set(true);
    store.conversation.set({
      ...CONVERSATION,
      messages: [
        { id: 'a', role: 'User', content: 'a question', payload: null, sequence: 0, createdAt: '2026-07-31T09:00:00Z' },
        { id: 'b', role: 'Assistant', content: ASSISTANT_NOT_CONNECTED, payload: null, sequence: 1, createdAt: '2026-07-31T09:00:00Z' },
      ],
    });
    fixture.detectChanges();

    const text: string = fixture.nativeElement.querySelector('[data-testid="assistant-messages"]').textContent;
    expect(text).not.toContain(ASSISTANT_NOT_CONNECTED);
    expect(text).toContain('a question');
  });

  // ── ★ The loader: real steps, or the plain one ─────────────────────────────

  /** Puts the panel in the state it is in while an answer is being worked on. */
  function waiting(steps: { phase: string; done: boolean }[]) {
    store.isOpen.set(true);
    store.conversation.set(CONVERSATION);
    // '' is "the request is out and nothing has come back" — the only state the loader renders in.
    store.setStreamState(store.activeDraftKey(), { reply: '' });
    store.setStreamState(store.activeDraftKey(), { steps });
    fixture.detectChanges();
  }

  it('★ renders one item per reported step, and marks the finished ones done', () => {
    waiting([
      { phase: 'understanding', done: true },
      { phase: 'reading_docs', done: true },
      { phase: 'searching_data', done: false },
    ]);

    const list = fixture.nativeElement.querySelector('[data-testid="assistant-steps"]');
    expect(list).withContext('a turn that reported real work shows what it did').toBeTruthy();

    const items = Array.from(list.querySelectorAll('li')) as HTMLElement[];
    expect(items.map((i) => i.getAttribute('data-phase')))
      .toEqual(['understanding', 'reading_docs', 'searching_data']);
    // ★ Finished steps carry the tick; the one still running carries the spinner. Getting this
    // backwards would show a green mark beside work that has not happened.
    expect(items.map((i) => i.getAttribute('data-state')))
      .toEqual(['done', 'done', 'running']);
    expect(items[0].querySelector('app-icon')).withContext('a done step shows the check').toBeTruthy();
    expect(items[2].querySelector('.assistant-steps__spinner'))
      .withContext('a running step shows the spinner').toBeTruthy();

    // The plain loader is NOT also on screen — one statement about the wait, not two.
    expect(fixture.nativeElement.querySelector('[data-testid="assistant-waiting"]')).toBeNull();
  });

  it('★ falls back to the plain loader when the turn reported no steps — old backend included', () => {
    // The compatibility branch, and the simple-question branch: a stream without progress frames must
    // look exactly like it did before they existed.
    waiting([]);

    expect(fixture.nativeElement.querySelector('[data-testid="assistant-steps"]')).toBeNull();
    expect(fixture.nativeElement.querySelector('[data-testid="assistant-waiting"]')).toBeTruthy();
  });

  it('does not turn two steps into a checklist', () => {
    // Understand, then answer — what every turn does. A two-line list that appears, ticks and vanishes
    // on every single request is movement without information, so the plain loader stands in.
    waiting([
      { phase: 'understanding', done: true },
      { phase: 'generating', done: false },
    ]);

    expect(fixture.nativeElement.querySelector('[data-testid="assistant-steps"]')).toBeNull();
    expect(fixture.nativeElement.querySelector('[data-testid="assistant-waiting"]')).toBeTruthy();
  });

  it('★ never prints a raw phase identifier, and shows an unknown phase rather than hiding it', () => {
    // Phases are identifiers translated here, exactly like the error keys. A newer backend reporting a
    // step this build has never heard of must still show something honest — hiding it would make the
    // panel look stalled during the very work it was told about.
    waiting([
      { phase: 'understanding', done: true },
      { phase: 'searching_data', done: true },
      { phase: 'something_new', done: false },
    ]);

    const text: string = fixture.nativeElement.querySelector('[data-testid="assistant-steps"]').textContent;
    expect(text).not.toContain('searching_data');
    expect(text).not.toContain('something_new');
    expect(fixture.nativeElement.querySelectorAll('[data-testid="assistant-steps"] li').length).toBe(3);
  });

  it('★ the steps disappear the moment the answer starts arriving', () => {
    store.isOpen.set(true);
    store.conversation.set(CONVERSATION);
    store.setStreamState(store.activeDraftKey(), {
      steps: [
        { phase: 'understanding', done: true },
        { phase: 'reading_docs', done: true },
        { phase: 'generating', done: false },
      ],
    });
    // The first fragment landed: the bubble now holds the answer, not the wait.
    store.setStreamState(store.activeDraftKey(), { reply: 'The answer begins' });
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('[data-testid="assistant-steps"]')).toBeNull();
    expect(fixture.nativeElement.querySelector('[data-testid="assistant-streaming"]').textContent)
      .toContain('The answer begins');
  });

  it('★ the composer is the multi-line primitive, and sending still works through it', async () => {
    // The migration test: WsInput (one line) → WsTextarea. What must NOT change is the send flow —
    // Enter still produces the same call, the same two stored turns come back, and the placeholder
    // is persisted exactly as in piece 1. Only the input element changed.
    const api = TestBed.inject(AssistantApiService) as jasmine.SpyObj<AssistantApiService>;
    store.isOpen.set(true);
    store.conversation.set(CONVERSATION);
    fixture.detectChanges();

    const composer: HTMLTextAreaElement = fixture.nativeElement.querySelector(
      '[data-testid="assistant-composer"] textarea',
    );
    expect(composer).withContext('the composer must be a textarea, not a single-line input').toBeTruthy();

    // Type a genuine multi-line message — the thing the one-line composer could not accept.
    composer.value = 'line one\nline two';
    composer.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    // Enter sends (Shift+Enter would have inserted a break — that contract is pinned in the
    // primitive's own spec).
    composer.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true, cancelable: true }));
    await fixture.whenStable();

    expect(api.streamMessage.calls.mostRecent().args.slice(0, 2)).toEqual(['conv-1', 'line one\nline two']);
    expect(store.messages().map((m) => m.id)).toEqual(['m-user', 'm-bot']);
    expect(store.messages()[1].content).toBe('A real answer.');
  });

  // ── The composer's presentation ─────────────────────────────────────────

  function openComposer() {
    store.isOpen.set(true);
    store.conversation.set(CONVERSATION);
    fixture.detectChanges();
  }

  function sendButton(): HTMLButtonElement {
    return fixture.nativeElement.querySelector('[data-testid="assistant-send"] button');
  }

  it('★ the send button is disabled while the box is empty and enabled once there is text', () => {
    openComposer();
    expect(sendButton().disabled).withContext('nothing to send yet').toBeTrue();

    const composer: HTMLTextAreaElement = fixture.nativeElement.querySelector(
      '[data-testid="assistant-composer"] textarea',
    );
    composer.value = 'a question';
    composer.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    expect(sendButton().disabled).toBeFalse();

    // Whitespace is not content: a box holding three spaces must not offer to send them.
    composer.value = '   ';
    composer.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    expect(sendButton().disabled).withContext('whitespace is not a message').toBeTrue();
  });

  // ── Scroll to the newest message ────────────────────────────────────────
  //
  // Headless lays out, but the fixture's message list never grows tall enough to actually overflow,
  // so these pin the WIRING: that the panel asks to be taken to the bottom, at the right moments,
  // with the right behaviour — and that it does so AFTER the render, not during the effect (which is
  // the mistake that makes a scroll-to-bottom "work sometimes").

  // The scroll now belongs to the CONVERSATION component, which the panel renders as a child — the
  // drawer is chrome around it. The suite still drives the panel, because that is what a user opens,
  // so the spy has to reach through to the child that actually owns the behaviour.
  // The scroll now belongs to the CONVERSATION component, which the panel renders as a child — the
  // drawer is chrome around it. The suite still drives the panel, because that is what a user opens.
  //
  // ★ THE PROTOTYPE, NOT AN INSTANCE. The panel renders nothing until it is open, so at the moment
  // these tests install the spy there is no child to install it on; spying on the class covers the
  // instance that is created a few lines later, which is the one that does the scrolling.
  function scrollSpy() {
    return spyOn(AssistantConversationComponent.prototype, 'scrollToBottom');
  }

  /** afterNextRender hooks run on the application tick, not on a bare detectChanges(). */
  async function render() {
    fixture.detectChanges();
    TestBed.inject(ApplicationRef).tick();
    await fixture.whenStable();
  }

  it('★ scrolls to the newest message when a conversation is opened, with no visible travel', async () => {
    const spy = scrollSpy();

    store.isOpen.set(true);
    store.conversation.set({
      ...CONVERSATION,
      messages: [
        { id: 'a', role: 'User', content: 'older', payload: null, sequence: 0, createdAt: '' },
        { id: 'b', role: 'Assistant', content: ASSISTANT_NOT_CONNECTED, payload: null, sequence: 1, createdAt: '' },
      ],
    });
    await render();

    expect(spy).toHaveBeenCalled();
    // 'auto', not 'smooth': the user must find the newest message already in place, never watch the
    // view travel down from the oldest one.
    expect(spy.calls.mostRecent().args[0]).toBe('auto');
  });

  it('★ scrolls again when a message is sent', async () => {
    store.isOpen.set(true);
    store.conversation.set(CONVERSATION);
    await render();

    const spy = scrollSpy();
    await store.send('hello');
    await render();

    expect(spy).toHaveBeenCalled();
    // Smooth here: the turn arrives while the user is watching, so the movement is informative.
    expect(spy.calls.mostRecent().args[0]).toBe('smooth');
  });

  it('★ re-runs when switching to another conversation', async () => {
    store.isOpen.set(true);
    store.conversation.set(CONVERSATION);
    await render();

    const spy = scrollSpy();
    store.conversation.set({ ...CONVERSATION, id: 'conv-2', title: 'Another' });
    await render();

    expect(spy).toHaveBeenCalled();
    expect(spy.calls.mostRecent().args[0]).toBe('auto');
  });

  it('does not yank the user down while they are reading older messages', async () => {
    // The nuance: a reply arriving must not tear someone away from the history they scrolled up to.
    store.isOpen.set(true);
    store.conversation.set(CONVERSATION);
    await render();

    const container: HTMLElement = fixture.nativeElement.querySelector('[data-testid="assistant-messages"]');
    // Simulate "scrolled up to read": far from the bottom.
    Object.defineProperty(container, 'scrollHeight', { value: 2000, configurable: true });
    Object.defineProperty(container, 'clientHeight', { value: 400, configurable: true });
    Object.defineProperty(container, 'scrollTop', { value: 0, configurable: true, writable: true });

    const spy = scrollSpy();
    await store.send('a new turn');
    await render();

    expect(spy).not.toHaveBeenCalled();
  });

  it('still follows along for a user who IS at the bottom', async () => {
    // The other side of the same rule — otherwise "do not yank" would quietly break the normal case.
    store.isOpen.set(true);
    store.conversation.set(CONVERSATION);
    await render();

    const container: HTMLElement = fixture.nativeElement.querySelector('[data-testid="assistant-messages"]');
    Object.defineProperty(container, 'scrollHeight', { value: 2000, configurable: true });
    Object.defineProperty(container, 'clientHeight', { value: 400, configurable: true });
    Object.defineProperty(container, 'scrollTop', { value: 1600, configurable: true, writable: true });

    const spy = scrollSpy();
    await store.send('a new turn');
    await render();

    expect(spy).toHaveBeenCalled();
  });

  it('★ the composer shrinks back to its original size after the message is sent', async () => {
    // ★ The bug as the user meets it: type a paragraph, press Enter, and the box stays as tall as the
    // text that just left it — eating the conversation above it a little more on every send.
    // Asserted end to end through the panel, because that is where the symptom lives; the primitive
    // has its own unit test for the same collapse.
    document.body.appendChild(fixture.nativeElement);
    openComposer();

    const composer: HTMLTextAreaElement = fixture.nativeElement.querySelector(
      '[data-testid="assistant-composer"] textarea',
    );
    const restingHeight = composer.getBoundingClientRect().height;

    composer.value = ['one', 'two', 'three', 'four', 'five', 'six'].join('\n');
    composer.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    const grownHeight = composer.getBoundingClientRect().height;
    expect(grownHeight).withContext('six lines must have grown the box').toBeGreaterThan(restingHeight);

    composer.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true, cancelable: true }));
    await fixture.whenStable();
    fixture.detectChanges();

    const afterSend = composer.getBoundingClientRect().height;
    document.body.removeChild(fixture.nativeElement);

    expect(afterSend).withContext('sending must return the composer to its original size').toBe(restingHeight);
  });

  it('renders the disclaimer ALWAYS, whatever the state', () => {
    // Not collapsible, not first-run-only: a permanent guard rail. Checked in the two states that
    // could plausibly hide it — a fresh empty panel and a conversation in progress.
    store.isOpen.set(true);
    store.conversation.set(null);
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('[data-testid="assistant-disclaimer"]')).toBeTruthy();

    // ...and it resolves THROUGH i18n rather than being hardcoded in the template: with a locale
    // loaded, the rendered text is the translation, not the key.
    const translate = TestBed.inject(TranslateService);
    translate.setTranslation('es', {
      ASSISTANT: { DISCLAIMER: 'El asistente solo orienta y responde consultas.' },
    });
    translate.use('es');
    openComposer();

    const disclaimer = fixture.nativeElement.querySelector('[data-testid="assistant-disclaimer"]');
    expect(disclaimer).toBeTruthy();
    expect(disclaimer.textContent.trim()).toBe('El asistente solo orienta y responde consultas.');
  });

  it('★ carries NO controls the assistant cannot perform', () => {
    // The visual reference ships Search, Deep Research, Reason, file upload and a microphone. This
    // assistant does none of them and several it never will — a control that does nothing is a
    // promise the engine cannot keep. This test is what stops them reappearing via copy-paste.
    openComposer();
    const card: HTMLElement = fixture.nativeElement.querySelector('[data-testid="assistant-composer-card"]');

    expect(card.querySelector('input[type="file"]')).withContext('no file upload').toBeNull();

    const buttons = Array.from(card.querySelectorAll('button')) as HTMLButtonElement[];
    // Exactly one button in the composer: send.
    expect(buttons.length).withContext('the composer holds send and nothing else').toBe(1);

    const label = (b: HTMLButtonElement) =>
      `${b.textContent ?? ''} ${b.getAttribute('aria-label') ?? ''} ${b.getAttribute('title') ?? ''}`.toLowerCase();
    const forbidden = ['search', 'research', 'reason', 'upload', 'attach', 'file', 'mic', 'voice', 'record'];
    for (const button of buttons) {
      for (const word of forbidden) {
        expect(label(button)).withContext(`no "${word}" control belongs here`).not.toContain(word);
      }
    }
  });

  // ── Rendered Markdown must actually be STYLED ────────────────────────────

  /** Renders one assistant message and returns the element for a CSS selector inside it. */
  function renderMarkdown(markdown: string): HTMLElement {
    store.isOpen.set(true);
    store.conversation.set({
      ...CONVERSATION,
      messages: [
        { id: 'md', role: 'Assistant', content: markdown, payload: null, sequence: 0, createdAt: '' },
      ],
    });
    // Attached to the document because getComputedStyle only resolves for elements in the page.
    document.body.appendChild(fixture.nativeElement);
    fixture.detectChanges();
    return fixture.nativeElement.querySelector('.assistant-msg__markdown');
  }

  it('★ the rendered table is actually STYLED, not just rendered', () => {
    // ★ THE BUG THIS PINS. Angular's default (Emulated) encapsulation compiles
    // `.assistant-msg__markdown td` into `.assistant-msg__markdown[_ngcontent-x] td[_ngcontent-x]` —
    // and HTML injected through [innerHTML] carries NO _ngcontent attribute, so every one of those
    // rules silently matched nothing. The table rendered as a real <table> and looked like two columns
    // of cramped text. A test that only asserted "a <table> exists" passed the whole time; only asking
    // the browser what it COMPUTED catches it.
    const host = renderMarkdown('| Función | Descripción |\n| --- | --- |\n| Planes | Reglas de comisión |');

    const cell = host.querySelector('td') as HTMLElement;
    const header = host.querySelector('th') as HTMLElement;
    expect(cell).withContext('the table renders').toBeTruthy();

    const cellStyle = getComputedStyle(cell);
    const headerStyle = getComputedStyle(header);

    expect(cellStyle.paddingLeft).not.toBe('0px', 'cells need breathing room to read as a table');
    // WIDTH, not style: Tailwind's preflight sets `border-style: solid` with `border-width: 0` on
    // every element, so asserting the style would pass with no rule of ours applied at all.
    expect(cellStyle.borderTopWidth).not.toBe('0px', 'cells need separating lines');
    expect(headerStyle.fontWeight).not.toBe('400', 'the header row must stand out');

    document.body.removeChild(fixture.nativeElement);
  });

  it('★ code blocks and inline code are styled and contained', () => {
    const host = renderMarkdown('Use `PlanStatus`:\n\n```csharp\nvar reallyLongLine = 1;\n```');

    const pre = host.querySelector('pre') as HTMLElement;
    const inline = host.querySelector('p code') as HTMLElement;

    expect(getComputedStyle(inline).backgroundColor).not.toBe('rgba(0, 0, 0, 0)', 'inline code needs a tint');
    // ★ Containment: a long line must scroll inside the bubble, never widen the 420px panel.
    expect(getComputedStyle(pre).overflowX).toBe('auto');

    document.body.removeChild(fixture.nativeElement);
  });

  it('★ links and lists are styled', () => {
    const host = renderMarkdown('- one\n- two\n\n[guide](https://example.com)');

    const list = host.querySelector('ul') as HTMLElement;
    const link = host.querySelector('a') as HTMLElement;

    expect(getComputedStyle(list).paddingLeft).not.toBe('0px', 'bullets need an indent to hang on');
    expect(getComputedStyle(list).listStyleType).not.toBe('none');
    expect(getComputedStyle(link).textDecorationLine).toContain('underline');

    document.body.removeChild(fixture.nativeElement);
  });

  it("every scrolling container in the panel wears the design system's scrollbar", () => {
    // ★ REUSED, NOT REINVENTED. `ws-scroll-thin` is the app's one scrollbar (styles.scss), already on
    // the sidebar, the modal body, the select list and the data table. The panel was showing the
    // browser's native bars beside them. Asserted by CLASS because that is how the utility is applied
    // everywhere else — a test on computed colours would pass just as well against a copy of the rules,
    // which is exactly what must not happen.
    store.isOpen.set(true);
    store.historyOpen.set(true);
    store.conversations.set([
      { id: 'c1', title: 'One', createdAt: '', updatedAt: '', messageCount: 2 },
    ]);
    fixture.detectChanges();

    const messages = fixture.nativeElement.querySelector('[data-testid="assistant-messages"]');
    const history = fixture.nativeElement.querySelector('[data-testid="assistant-history"]');
    const composer = fixture.nativeElement.querySelector('[data-testid="assistant-composer"] textarea');

    expect(messages.classList).toContain('ws-scroll-thin');
    expect(history.classList).toContain('ws-scroll-thin', 'the conversation list scrolls too');
    expect(composer.classList).toContain('ws-scroll-thin', 'a long draft scrolls inside the composer');
  });

  it('★ the ::ng-deep styles do NOT escape the chat bubble', () => {
    // ★ THE CONTAINMENT TEST. ::ng-deep pierces encapsulation, and an unscoped one here would restyle
    // every table, list and link in Wasnie from a chat panel — borders and padding appearing on the
    // payouts grid because someone asked the assistant a question. Asserted by rendering an identical
    // table OUTSIDE the panel and checking it was left alone.
    const host = renderMarkdown('| A | B |\n| --- | --- |\n| 1 | 2 |');
    const inside = getComputedStyle(host.querySelector('td') as HTMLElement);

    const outsider = document.createElement('div');
    outsider.innerHTML = '<table><tbody><tr><td id="outsider-cell">1</td></tr></tbody></table>';
    document.body.appendChild(outsider);
    const outside = getComputedStyle(outsider.querySelector('td') as HTMLElement);

    // The bubble's cell is styled…
    expect(inside.paddingLeft).not.toBe('0px');
    // …and the identical cell outside it is untouched.
    expect(outside.paddingLeft).toBe('0px', 'the chat must not restyle tables elsewhere in the app');
    // Again width, not style — the `solid` here is Tailwind's preflight, present app-wide and nothing
    // to do with these rules.
    expect(outside.borderTopWidth).toBe('0px');

    document.body.removeChild(outsider);
    document.body.removeChild(fixture.nativeElement);
  });

  it('★ shows a translated label for an untitled thread, never the sentinel', () => {
    // ★ The backend stores `__UNTITLED__` so the history is not frozen into one user's language. If
    // that ever reaches the screen it looks like a bug — this is what stops it.
    store.isOpen.set(true);
    store.historyOpen.set(true);
    store.conversation.set({ ...CONVERSATION, title: '__UNTITLED__' });
    store.conversations.set([
      { id: 'conv-1', title: '__UNTITLED__', createdAt: '', updatedAt: '', messageCount: 0 },
    ]);
    fixture.detectChanges();

    const header: string = fixture.nativeElement.querySelector('.assistant-panel__title').textContent;
    const historyRow: string = fixture.nativeElement.querySelector('[data-testid="assistant-history-item"]').textContent;

    expect(header).not.toContain('__UNTITLED__');
    expect(historyRow).not.toContain('__UNTITLED__');
    // The i18n key resolves (TranslateModule with no locale echoes the key, which is still not the sentinel).
    expect(header).toContain('UNTITLED');
  });

  it('★ marks the OPEN conversation with the brand edge, so hover cannot impersonate it', () => {
    // ★★ THE DEFECT THIS GUARDS. Hover and active both filled the row with
    // --color-bg-surface-sunken, so pointing at any row made it look exactly like the one being read
    // and the rail stopped answering "which one am I on?" the moment the mouse entered the list.
    //
    // ★ AND WHY THE ASSERTION IS THE EDGE AND NOT THE FILL. In the soft theme
    // --color-bg-surface-hover and --color-bg-surface-sunken are THE SAME COLOUR (styles.scss), so a
    // fix that only swapped fills would be correct in two themes and invisible in the third. The brand
    // inset is what separates them everywhere — the sidebar's own convention for the current item.
    //
    // :hover cannot be triggered from script, so the hover half is not assertable here; this pins the
    // half that carries the distinction in every theme.
    store.isOpen.set(true);
    store.historyOpen.set(true);
    store.conversation.set({ ...CONVERSATION, id: 'conv-1' });
    store.conversations.set([
      { id: 'conv-1', title: 'The open one', createdAt: '', updatedAt: '', messageCount: 0 },
      { id: 'conv-2', title: 'Another', createdAt: '', updatedAt: '', messageCount: 0 },
    ]);
    fixture.detectChanges();

    document.body.appendChild(fixture.nativeElement);

    const rows = fixture.nativeElement.querySelectorAll('[data-testid="assistant-history-item"]');
    const open = getComputedStyle(rows[0] as HTMLElement);
    const other = getComputedStyle(rows[1] as HTMLElement);

    expect(open.boxShadow).not.toBe('none',
      'the open conversation carries the brand edge');
    expect(other.boxShadow).toBe('none',
      'a row nobody is reading carries no marker at all');

    // It is an INSET shadow, not a border: a 3px left border would square the row’s radius and
    // shove the title sideways on every selection.
    expect(open.boxShadow).toContain('inset');

    document.body.removeChild(fixture.nativeElement);
  });

  it('shows a real title as it was stored', () => {
    store.isOpen.set(true);
    store.conversation.set({ ...CONVERSATION, title: '¿Cómo creo un plan?' });
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('.assistant-panel__title').textContent)
      .toContain('¿Cómo creo un plan?');
  });

  // ★ THE ROW'S CONTRACT CHANGED, AND THIS TEST CHANGED WITH IT — deliberately, not because it was in
  // the way. It used to assert title + meta + a permanent delete icon. The rail redesign says a row
  // shows ONE thing at rest, its title, and keeps rename and delete behind a menu that appears on
  // hover: two permanent icons per row turned the list into a grid of controls where the titles were
  // meant to be, and a loose pencil under each title was the visible symptom. What still has to hold
  // is that no row loses its title and no row loses its way to those actions.
  it('every row shows its title, with the actions behind a menu rather than loose icons', () => {
    store.isOpen.set(true);
    store.historyOpen.set(true);
    store.conversations.set([
      { id: 'c1', title: 'Primera', createdAt: '', updatedAt: '2026-08-03T10:00:00Z', messageCount: 4 },
      { id: 'c2', title: 'Segunda', createdAt: '', updatedAt: '2026-08-02T10:00:00Z', messageCount: 2 },
    ]);
    fixture.detectChanges();

    const rows = fixture.nativeElement.querySelectorAll('[data-testid="assistant-history-item"]');
    expect(rows.length).toBe(2);

    for (const row of rows) {
      expect(row.querySelector('.assistant-clist__item-title')).toBeTruthy();
      expect(row.querySelector('[data-testid="assistant-item-menu"]')).toBeTruthy();
    }

    expect(rows[0].querySelector('.assistant-clist__item-title').textContent).toContain('Primera');
  });

  // The pencil that started this: it must not be sitting in the row any more.
  it('★ no rename or delete control sits loose in the row', () => {
    store.isOpen.set(true);
    store.historyOpen.set(true);
    store.conversations.set([
      { id: 'c1', title: 'Primera', createdAt: '', updatedAt: '2026-08-03T10:00:00Z', messageCount: 4 },
    ]);
    fixture.detectChanges();

    const row = fixture.nativeElement.querySelector('[data-testid="assistant-history-item"]');

    expect(row.querySelector('[data-testid="assistant-rename-start"]'))
      .withContext('rename belongs in the menu, not in the row').toBeNull();
    expect(row.querySelector('[data-testid="assistant-item-delete"]'))
      .withContext('delete belongs in the menu, not in the row').toBeNull();
  });

  it('identifies a placeholder reply only for the assistant role', () => {
    expect(isPlaceholderReply({
      id: 'x', role: 'Assistant', content: ASSISTANT_NOT_CONNECTED, payload: null, sequence: 0, createdAt: '',
    })).toBeTrue();

    // A user who literally types the marker is not the assistant, and must be shown their own words.
    expect(isPlaceholderReply({
      id: 'y', role: 'User', content: ASSISTANT_NOT_CONNECTED, payload: null, sequence: 0, createdAt: '',
    })).toBeFalse();
  });
});

/**
 * ★ PILLAR 3 — the link interceptor.
 *
 * The assistant now answers "how do I create a plan?" with steps and a link to `/plans/new`. Rendered
 * Markdown gives that a plain `<a href>`, and a plain `<a href>` is a FULL PAGE LOAD: Angular is torn
 * down and rebuilt, and the conversation that just told the user where to go is gone at the exact
 * moment they acted on it. These tests pin that internal links route through Angular instead, and that
 * external ones are left entirely alone.
 */
describe('AssistantPanelComponent — assistant links navigate without reloading the app', () => {
  let fixture: ComponentFixture<AssistantPanelComponent>;
  let store: AssistantStore;
  let router: Router;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [AssistantPanelComponent, TranslateModule.forRoot()],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: AssistantApiService, useValue: apiSpy() },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(AssistantPanelComponent);
    store = TestBed.inject(AssistantStore);
    router = TestBed.inject(Router);
    spyOn(router, 'navigateByUrl').and.resolveTo(true);
  });

  /** Renders one assistant reply and hands back the anchor inside it. */
  function anchorFor(markdown: string): HTMLAnchorElement {
    store.isOpen.set(true);
    store.conversation.set({
      ...CONVERSATION,
      messages: [
        { id: 'm1', role: 'Assistant', content: markdown, payload: null, sequence: 0, createdAt: '' },
      ],
    });
    fixture.detectChanges();

    const anchor: HTMLAnchorElement | null =
      fixture.nativeElement.querySelector('.assistant-msg__markdown a');
    expect(anchor).withContext('the reply must actually render a link').toBeTruthy();
    return anchor!;
  }

  /**
   * Clicks the anchor and reports what the component did with the event.
   *
   * The listener on `document` runs AFTER the panel's own (which sits on the bubble container), so it
   * reads an honest `defaultPrevented` — and then prevents the default itself, so a link the component
   * deliberately left alone does not navigate the Karma page or open a popup mid-suite.
   */
  function clickAndObserve(anchor: HTMLAnchorElement): { preventedByPanel: boolean } {
    let preventedByPanel = false;
    const guard = (ev: Event) => {
      preventedByPanel = ev.defaultPrevented;
      ev.preventDefault();
    };
    document.addEventListener('click', guard);
    try {
      anchor.dispatchEvent(new MouseEvent('click', { bubbles: true, cancelable: true, button: 0 }));
    } finally {
      document.removeEventListener('click', guard);
    }
    return { preventedByPanel };
  }

  it('★ an internal link is routed through Angular, not followed by the browser', () => {
    // ★ THE TEST THIS FEATURE STANDS ON. Remove the (click) handler from the markdown container and
    // this goes red — which is the moment the browser starts reloading the whole app and wiping the
    // chat on the most valuable click in the product.
    const anchor = anchorFor('Then [Go to new plan](/plans/new).');

    expect(anchor.getAttribute('href')).toBe('/plans/new');

    const { preventedByPanel } = clickAndObserve(anchor);

    expect(preventedByPanel).withContext('the browser must not be allowed to load the page').toBeTrue();
    expect(router.navigateByUrl).toHaveBeenCalledWith('/plans/new');
  });

  it('an internal link does NOT open in a new tab', () => {
    // The destination is the app the user is already inside. A second copy of it in a new tab is not
    // what "go here" means — and target=_blank would fight the interceptor for the same click.
    const anchor = anchorFor('[Plans](/plans)');

    expect(anchor.getAttribute('target')).toBeNull();
  });

  it('★ an EXTERNAL link is not intercepted, and keeps its noopener', () => {
    // The router is for this app. Anything else is the browser's job — and it still opens in a new tab
    // with the hardening the Markdown work put there, because a model-authored page must not be able
    // to reach back through window.opener and navigate Wasnie somewhere of its choosing.
    const anchor = anchorFor('See [the docs](https://externo.com/guide).');

    expect(anchor.getAttribute('target')).toBe('_blank');
    expect(anchor.getAttribute('rel')).toContain('noopener');

    const { preventedByPanel } = clickAndObserve(anchor);

    expect(preventedByPanel).withContext('an external link is the browser\'s business').toBeFalse();
    expect(router.navigateByUrl).not.toHaveBeenCalled();
  });

  it('★ a protocol-relative URL is EXTERNAL, however much it looks like a route', () => {
    // ★ THE TRAP IN THE NAIVE CHECK. `//evil.com` starts with a slash and is not a path: the browser
    // resolves it to `https://evil.com`. A leading-slash test alone would hand a model-authored
    // destination straight to the app's own router.
    const anchor = anchorFor('[looks internal](//evil.com/x)');

    const { preventedByPanel } = clickAndObserve(anchor);

    expect(preventedByPanel).toBeFalse();
    expect(router.navigateByUrl).not.toHaveBeenCalled();
    expect(anchor.getAttribute('target')).toBe('_blank');
  });

  it('a ctrl-clicked internal link is left to the browser', () => {
    // The user asked for a new tab on purpose. Hijacking that into an in-app navigation overrides an
    // intent they expressed deliberately.
    const anchor = anchorFor('[Plans](/plans)');

    let preventedByPanel = false;
    const guard = (ev: Event) => {
      preventedByPanel = ev.defaultPrevented;
      ev.preventDefault();
    };
    document.addEventListener('click', guard);
    anchor.dispatchEvent(
      new MouseEvent('click', { bubbles: true, cancelable: true, button: 0, ctrlKey: true }));
    document.removeEventListener('click', guard);

    expect(preventedByPanel).toBeFalse();
    expect(router.navigateByUrl).not.toHaveBeenCalled();
  });

  it('a click on the prose around a link does nothing', () => {
    anchorFor('Some text and a [link](/plans).');

    const container: HTMLElement = fixture.nativeElement.querySelector('.assistant-msg__markdown');
    container.dispatchEvent(new MouseEvent('click', { bubbles: true, cancelable: true, button: 0 }));

    expect(router.navigateByUrl).not.toHaveBeenCalled();
  });
});

/**
 * ★ RESILIENCE — the way out of a rate limit.
 *
 * The backend commits the user's turn BEFORE it calls the model, so a 429 leaves the question stored
 * and no answer. These pin that Retry RE-ANSWERS that stored turn rather than sending it again — the
 * difference between a recovered conversation and one where the user's own words appear twice.
 */
describe('AssistantStore — retry after a failed answer', () => {
  let store: AssistantStore;
  let api: jasmine.SpyObj<AssistantApiService>;

  beforeEach(() => {
    api = apiSpy();
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: AssistantApiService, useValue: api },
      ],
    });
    store = TestBed.inject(AssistantStore);
  });

  /** A stream that stores the question and then fails — exactly what a 429 produces. */
  function rateLimited(): void {
    api.streamMessage.and.callFake((_id: string, content: string) =>
      frames([
        { type: 'user', message: exchange(content).userMessage },
        { type: 'error', errorKey: 'ASSISTANT.ERROR_RATE_LIMITED' },
      ]));
  }

  it('offers a retry after a rate-limited answer, and not before', async () => {
    expect(store.retryable()).toBeNull();

    rateLimited();
    await store.startConversation();
    await store.send('what happened with TERM-CC-10?');

    expect(store.errorKey()).toBe('ASSISTANT.ERROR_RATE_LIMITED');
    expect(store.retryable()).not.toBeNull();
    // The question survived — which is what the message on screen promises.
    expect(store.messages().map((m) => m.role)).toEqual(['User']);
  });

  it('★ the retry RE-ANSWERS the stored turn instead of sending it again', async () => {
    rateLimited();
    await store.startConversation();
    await store.send('what happened with TERM-CC-10?');

    // The provider recovers.
    api.streamMessage.and.callFake((_id: string, content: string) =>
      frames([
        { type: 'delta', delta: 'It was paid.' },
        { type: 'done', message: { ...exchange(content).assistantMessage, content: 'It was paid.' } },
      ]));

    await store.retry();

    // ★ isRetry is true, which is what tells the server not to store the question a second time.
    expect(api.streamMessage.calls.mostRecent().args[4]).toBeTrue();

    // ONE user row, still — and the answer beside it.
    expect(store.messages().map((m) => m.role)).toEqual(['User', 'Assistant']);
    expect(store.messages().filter((m) => m.role === 'User').length).toBe(1);
    // Nothing left to retry once it worked.
    expect(store.retryable()).toBeNull();
  });

  it('a failure BEFORE the question reached the server retries as a normal send', async () => {
    // No `user` frame arrived, so nothing is stored and there is nothing to re-answer. Sending it
    // again is correct here — and telling the server "this is a retry" would be a lie it would act on
    // by re-answering a turn that does not exist.
    api.streamMessage.and.callFake(() =>
      frames([{ type: 'error', errorKey: 'ASSISTANT.ERROR_UNAVAILABLE' }]));

    await store.startConversation();
    await store.send('a question that never landed');

    expect(store.retryable()?.wasPersisted).toBeFalse();

    await store.retry();

    expect(api.streamMessage.calls.mostRecent().args[4]).toBeFalse();
    expect(api.streamMessage.calls.mostRecent().args[1]).toBe('a question that never landed');
  });

  it('does nothing when there is nothing to retry', async () => {
    await store.retry();
    expect(api.streamMessage).not.toHaveBeenCalled();
  });
});

describe('AssistantPanelComponent — the retry button', () => {
  let fixture: ComponentFixture<AssistantPanelComponent>;
  let store: AssistantStore;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [AssistantPanelComponent, TranslateModule.forRoot()],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: AssistantApiService, useValue: apiSpy() },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(AssistantPanelComponent);
    store = TestBed.inject(AssistantStore);
  });

  it('shows a NON-TECHNICAL message and a retry button when the assistant is rate limited', () => {
    // ★ What the user reads is a translated KEY, never the provider's sentence — a vendor error string
    // can carry request ids and slices of the prompt.
    store.isOpen.set(true);
    store.conversation.set(CONVERSATION);
    store.setStreamState(store.activeDraftKey(), { errorKey: 'ASSISTANT.ERROR_RATE_LIMITED' });
    store.conversation.set({
      ...CONVERSATION,
      messages: [
        {
          id: 'm1', role: 'User', content: 'what happened with TERM-CC-10?',
          payload: null, sequence: 0, createdAt: '',
        },
      ],
      lastTurnUnanswered: true,
    });
    fixture.detectChanges();

    const error = fixture.nativeElement.querySelector('[data-testid="assistant-error"]');
    expect(error).toBeTruthy();
    expect(error.textContent).toContain('ERROR_RATE_LIMITED');
    expect(error.textContent).not.toContain('429');
    expect(error.textContent).not.toContain('Groq');

    expect(fixture.nativeElement.querySelector('[data-testid="assistant-retry"]')).toBeTruthy();
  });

  it('hides the retry button when there is nothing to retry', () => {
    store.isOpen.set(true);
    store.conversation.set(CONVERSATION);
    store.setStreamState(store.activeDraftKey(), { errorKey: null });
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('[data-testid="assistant-retry"]')).toBeNull();
  });

  it('the button asks the store to retry', async () => {
    const spy = spyOn(store, 'retry').and.resolveTo();
    store.isOpen.set(true);
    store.conversation.set(CONVERSATION);
    store.setStreamState(store.activeDraftKey(), { errorKey: 'ASSISTANT.ERROR_RATE_LIMITED' });
    store.conversation.set({
      ...CONVERSATION,
      messages: [
        { id: 'm1', role: 'User', content: 'x', payload: null, sequence: 0, createdAt: '' },
      ],
      lastTurnUnanswered: true,
    });
    fixture.detectChanges();

    fixture.nativeElement
      .querySelector('[data-testid="assistant-retry"] button')
      ?.click() ?? fixture.nativeElement.querySelector('[data-testid="assistant-retry"]').click();

    expect(spy).toHaveBeenCalled();
  });
});

/**
 * ★ THE FAILURE SURVIVES A REFRESH.
 *
 * The bug this fixes: the assistant failed, the retry appeared, the user reloaded, and everything was
 * gone — no message, no warning, no way back. These pin that the state is reconstructed from what the
 * SERVER reports about the stored turns, with no help from session memory.
 */
describe('AssistantStore — a failed turn reloaded from the server', () => {
  let store: AssistantStore;
  let api: jasmine.SpyObj<AssistantApiService>;

  /** What the server returns for a thread whose last question was never answered. */
  const FAILED_THREAD: AssistantConversation = {
    ...CONVERSATION,
    id: 'conv-failed',
    messages: [
      {
        id: 'm1', role: 'User', content: 'what happened with TERM-CC-10?',
        payload: null, sequence: 0, createdAt: '2026-08-03T10:29:00Z',
      },
    ],
    lastTurnUnanswered: true,
  };

  beforeEach(() => {
    api = apiSpy();
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: AssistantApiService, useValue: api },
      ],
    });
    store = TestBed.inject(AssistantStore);
  });

  it('★ reconstructs the retry from a FRESH load, with no session memory behind it', async () => {
    // This store has never sent anything — exactly the state after a page refresh.
    api.getConversation.and.returnValue(of(FAILED_THREAD));

    await store.openConversation('conv-failed');

    const failed = store.retryable();
    expect(failed).withContext('the warning must come back after a refresh').not.toBeNull();
    expect(failed!.wasPersisted).toBeTrue();
    expect(failed!.content).toBe('what happened with TERM-CC-10?');
  });

  it('shows nothing to retry for a thread that was answered', async () => {
    api.getConversation.and.returnValue(of({
      ...FAILED_THREAD,
      messages: [
        ...FAILED_THREAD.messages,
        { id: 'm2', role: 'Assistant', content: 'It was paid.', payload: null, sequence: 1, createdAt: '' },
      ],
      lastTurnUnanswered: false,
    } as AssistantConversation));

    await store.openConversation('conv-failed');

    expect(store.retryable()).toBeNull();
  });

  it('★ retrying a RELOADED failure re-answers without duplicating the question', async () => {
    api.getConversation.and.returnValue(of(FAILED_THREAD));
    await store.openConversation('conv-failed');

    api.streamMessage.and.callFake(() =>
      frames([
        { type: 'delta', delta: 'It was paid.' },
        {
          type: 'done',
          message: {
            id: 'm2', role: 'Assistant', content: 'It was paid.',
            payload: null, sequence: 1, createdAt: '',
          },
        },
      ]));

    await store.retry();

    // isRetry = true: the server re-answers the stored turn instead of storing the question again.
    expect(api.streamMessage.calls.mostRecent().args[4]).toBeTrue();
    expect(store.messages().map((m) => m.role)).toEqual(['User', 'Assistant']);
    expect(store.messages().filter((m) => m.role === 'User').length).toBe(1);
    // ...and the warning is gone, without a second round trip to find that out.
    expect(store.retryable()).toBeNull();
  });
});

describe('AssistantPanelComponent — the failed-turn alert', () => {
  let fixture: ComponentFixture<AssistantPanelComponent>;
  let store: AssistantStore;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [AssistantPanelComponent, TranslateModule.forRoot()],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: AssistantApiService, useValue: apiSpy() },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(AssistantPanelComponent);
    store = TestBed.inject(AssistantStore);
  });

  /** Opens the panel on a thread the SERVER says ended on an unanswered question. */
  function renderReloadedFailure(): HTMLElement {
    store.isOpen.set(true);
    store.conversation.set({
      ...CONVERSATION,
      messages: [
        {
          id: 'm1', role: 'User', content: 'what happened with TERM-CC-10?',
          payload: null, sequence: 0, createdAt: '',
        },
      ],
      lastTurnUnanswered: true,
    });
    fixture.detectChanges();
    return fixture.nativeElement.querySelector('[data-testid="assistant-failed-alert"]');
  }

  it('★ renders the alert and the retry from the RELOADED state alone', () => {
    const alert = renderReloadedFailure();

    expect(alert).withContext('a refreshed page must still show what happened').toBeTruthy();
    expect(alert.getAttribute('role')).toBe('alert');
    expect(alert.querySelector('[data-testid="assistant-retry"]'))
      .withContext('the retry lives INSIDE the alert').toBeTruthy();
    // The question is still on screen beside it — nothing to retype.
    expect(fixture.nativeElement.textContent).toContain('what happened with TERM-CC-10?');
  });

  it('uses the design system WARNING tokens, not invented classes', () => {
    // ★ Asserted by COMPUTED style, not by class name: a class that exists but resolves to nothing is
    // exactly the failure mode of "styled with a token that was never defined" — the --shadow-card
    // problem this codebase already found once.
    const alert = renderReloadedFailure();
    const styles = getComputedStyle(alert);

    const warningBg = getComputedStyle(document.documentElement)
      .getPropertyValue('--color-warning-bg').trim();
    expect(warningBg).withContext('the token must actually be defined').not.toBe('');

    expect(styles.backgroundColor).not.toBe('rgba(0, 0, 0, 0)');
    expect(parseFloat(styles.borderTopWidth)).toBeGreaterThan(0);
    expect(parseFloat(styles.paddingTop)).toBeGreaterThan(0);
    expect(parseFloat(styles.paddingBottom)).toBeGreaterThan(0);
  });

  it('has NO dismiss control — the only ways out are retrying or asking again', () => {
    // ★ Dismissing would throw away the only route back to an answer and leave the question sitting
    // there with nothing under it: the exact state this fix removes, reached by the user's own click.
    const alert = renderReloadedFailure();

    const buttons = Array.from(alert.querySelectorAll('button')) as HTMLElement[];
    expect(buttons.length).withContext('retry, and nothing else').toBe(1);
    expect(alert.textContent).not.toContain('×');
  });

  it('falls back to a general explanation when the reason was not seen this session', () => {
    // After a refresh the reason was never stored, so claiming a specific one would be inventing it.
    const alert = renderReloadedFailure();

    expect(alert.textContent).toContain('FAILED_TITLE');
    expect(alert.textContent).toContain('FAILED_DESC');
  });

  it('shows the SPECIFIC reason when this session watched it fail', () => {
    // Keyed by the conversation, not by "whatever is open": nothing is open yet at this line.
    store.setStreamState(CONVERSATION.id, { errorKey: 'ASSISTANT.ERROR_RATE_LIMITED' });
    const alert = renderReloadedFailure();

    expect(alert.textContent).toContain('ERROR_RATE_LIMITED');
    expect(alert.textContent).not.toContain('FAILED_DESC');
  });

  it('is absent when nothing failed', () => {
    store.isOpen.set(true);
    store.conversation.set(CONVERSATION);
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('[data-testid="assistant-failed-alert"]')).toBeNull();
  });
});

/**
 * ★ THE FAILURE CARD MUST NOT APPEAR WHILE THE ANSWER IS ON ITS WAY.
 *
 * The bug: the moment the server echoed the stored question, the thread ended on an unanswered turn —
 * true, but only because the answer had not arrived yet. The card appeared beside the typing dots and
 * told the user their question had failed while it was being answered, then vanished when it landed.
 * "Waiting" is not "failed".
 */
describe('AssistantStore — no failure card while an answer is in flight', () => {
  let store: AssistantStore;
  let api: jasmine.SpyObj<AssistantApiService>;

  beforeEach(() => {
    api = apiSpy();
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: AssistantApiService, useValue: api },
      ],
    });
    store = TestBed.inject(AssistantStore);
  });

  it('★ stays hidden between the stored question and the answer', async () => {
    // The frames are yielded one at a time and `retryable()` is read after each — which is exactly
    // what the panel does as it re-renders. Any true in the middle is the card the user saw.
    const seen: boolean[] = [];

    api.streamMessage.and.callFake((_id: string, content: string) => {
      const list: AssistantStreamEvent[] = [
        { type: 'user', message: exchange(content).userMessage },
        { type: 'delta', delta: 'It was ' },
        { type: 'delta', delta: 'paid.' },
        { type: 'done', message: { ...exchange(content).assistantMessage, content: 'It was paid.' } },
      ];
      return (async function* () {
        for (const frame of list) {
          yield frame;
          seen.push(store.retryable() !== null);
        }
      })();
    });

    await store.startConversation();
    await store.send('what happened with TERM-CC-10?');

    expect(seen).withContext('the card must never flash mid-exchange').toEqual([false, false, false, false]);
    expect(store.retryable()).toBeNull();
  });

  it('appears only once the answer has actually failed', async () => {
    const seen: boolean[] = [];

    api.streamMessage.and.callFake((_id: string, content: string) => {
      const list: AssistantStreamEvent[] = [
        { type: 'user', message: exchange(content).userMessage },
        { type: 'error', errorKey: 'ASSISTANT.ERROR_RATE_LIMITED' },
      ];
      return (async function* () {
        for (const frame of list) {
          yield frame;
          seen.push(store.retryable() !== null);
        }
      })();
    });

    await store.startConversation();
    await store.send('what happened with TERM-CC-10?');

    // Hidden while the question was merely waiting; the error is what reveals it — and it stays after,
    // when `sending` is back to false.
    expect(seen).toEqual([false, false]);
    expect(store.retryable()).not.toBeNull();
    expect(store.conversation()!.lastTurnUnanswered)
      .withContext('and a refresh would derive the same thing').toBeTrue();
  });

  it('★ a RETRY of a reloaded failure hides the card while it runs', async () => {
    // This one starts with the thread already marked unanswered, so without the in-flight guard the
    // card would sit beside the very loader that is resolving it — inviting a second retry of the
    // request already running.
    api.getConversation.and.returnValue(of({
      ...CONVERSATION,
      messages: [
        { id: 'm1', role: 'User', content: 'q', payload: null, sequence: 0, createdAt: '' },
      ],
      lastTurnUnanswered: true,
    } as AssistantConversation));

    await store.openConversation('conv-1');
    expect(store.retryable()).withContext('visible before the retry starts').not.toBeNull();

    const seen: boolean[] = [];
    api.streamMessage.and.callFake(() => {
      const list: AssistantStreamEvent[] = [
        { type: 'delta', delta: 'It was paid.' },
        {
          type: 'done',
          message: { id: 'm2', role: 'Assistant', content: 'It was paid.', payload: null, sequence: 1, createdAt: '' },
        },
      ];
      return (async function* () {
        for (const frame of list) {
          yield frame;
          seen.push(store.retryable() !== null);
        }
      })();
    });

    await store.retry();

    expect(seen).withContext('hidden for the whole run').toEqual([false, false]);
    expect(store.retryable()).toBeNull();
  });
});

/**
 * ★ THE RETRY MUST LOOK LIKE IT IS WORKING.
 *
 * The typing dots are normally lit by the `user` frame, and a retry never sends one — the question is
 * already stored, and echoing it back would duplicate it on screen. So pressing Retry showed nothing
 * at all until the first fragment arrived: a button that looks broken at the exact moment it works.
 */
describe('AssistantStore — the retry shows the assistant is working', () => {
  let store: AssistantStore;
  let api: jasmine.SpyObj<AssistantApiService>;

  const FAILED_THREAD = {
    ...CONVERSATION,
    messages: [
      { id: 'm1', role: 'User' as const, content: 'q', payload: null, sequence: 0, createdAt: '' },
    ],
    lastTurnUnanswered: true,
  };

  beforeEach(() => {
    api = apiSpy();
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: AssistantApiService, useValue: api },
      ],
    });
    store = TestBed.inject(AssistantStore);
  });

  it('★ lights the typing indicator BEFORE the first fragment arrives', async () => {
    api.getConversation.and.returnValue(of(FAILED_THREAD as AssistantConversation));
    await store.openConversation('conv-1');

    // Captured at the instant the request is made — before any frame has come back. Null here is the
    // dead pause the user was seeing.
    let whenAsked: string | null | undefined;

    api.streamMessage.and.callFake(() => {
      whenAsked = store.streamingReply();
      return frames([
        { type: 'delta', delta: 'It was paid.' },
        {
          type: 'done',
          message: { id: 'm2', role: 'Assistant', content: 'It was paid.', payload: null, sequence: 1, createdAt: '' },
        },
      ]);
    });

    await store.retry();

    expect(whenAsked).withContext('the dots must be up the moment the button is pressed').toBe('');
    expect(store.streamingReply()).withContext('and down once the answer is stored').toBeNull();
    expect(store.messages().map((m) => m.role)).toEqual(['User', 'Assistant']);
  });

  it('takes the indicator down again when the retry ALSO fails', async () => {
    api.getConversation.and.returnValue(of(FAILED_THREAD as AssistantConversation));
    await store.openConversation('conv-1');

    api.streamMessage.and.callFake(() =>
      frames([{ type: 'error', errorKey: 'ASSISTANT.ERROR_RATE_LIMITED' }]));

    await store.retry();

    // No dots left spinning over an answer that will never come — and the card is back, so the user
    // can try once more.
    expect(store.streamingReply()).toBeNull();
    expect(store.retryable()).not.toBeNull();
    expect(store.errorKey()).toBe('ASSISTANT.ERROR_RATE_LIMITED');
  });

  it('an ordinary send still waits for the stored question before showing anything', async () => {
    // A first send has a `user` frame to light the dots, and lighting them earlier would put an
    // assistant bubble on screen before the question the user typed appeared beside it.
    let whenAsked: string | null | undefined;

    api.streamMessage.and.callFake((_id: string, content: string) => {
      whenAsked = store.streamingReply();
      return frames([
        { type: 'user', message: exchange(content).userMessage },
        { type: 'done', message: { ...exchange(content).assistantMessage, content: 'A real answer.' } },
      ]);
    });

    await store.startConversation();
    await store.send('hello');

    expect(whenAsked).toBeNull();
  });
});

describe('AssistantPanelComponent — the retry renders the typing bubble', () => {
  let fixture: ComponentFixture<AssistantPanelComponent>;
  let store: AssistantStore;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [AssistantPanelComponent, TranslateModule.forRoot()],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: AssistantApiService, useValue: apiSpy() },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(AssistantPanelComponent);
    store = TestBed.inject(AssistantStore);
  });

  it('★ shows the streaming bubble, and hides the failure card, while a retry runs', () => {
    // The state a retry is in one tick after the button: thread still marked unanswered, request in
    // flight, nothing streamed yet. The user must see the assistant working — not the card that told
    // them it had failed.
    store.isOpen.set(true);
    store.conversation.set({
      ...CONVERSATION,
      messages: [
        { id: 'm1', role: 'User', content: 'q', payload: null, sequence: 0, createdAt: '' },
      ],
      lastTurnUnanswered: true,
    });
    store.setStreamState(store.activeDraftKey(), { sending: true });
    store.setStreamState(store.activeDraftKey(), { reply: '' });
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('[data-testid="assistant-streaming"]'))
      .withContext('the typing bubble is the feedback the button owes the user').toBeTruthy();
    // The waiting mark — the assistant's avatar, which took the three dots' place.
    expect(fixture.nativeElement.querySelector('.assistant-msg__avatar')).toBeTruthy();
    expect(fixture.nativeElement.querySelector('[data-testid="assistant-failed-alert"]')).toBeNull();
  });
});

import enTranslations from '../../../assets/i18n/en.json';
import esTranslations from '../../../assets/i18n/es.json';
import { STARTER_PROMPTS, placeholderRange } from './panel/starter-prompts';
import plTranslations from '../../../assets/i18n/pl.json';

/** The shipped ASSISTANT sections, keyed by language. */
const ASSISTANT_BUNDLES: Record<string, Record<string, string>> = {
  en: (enTranslations as unknown as Record<string, Record<string, string>>)['ASSISTANT'],
  es: (esTranslations as unknown as Record<string, Record<string, string>>)['ASSISTANT'],
  pl: (plTranslations as unknown as Record<string, Record<string, string>>)['ASSISTANT'],
};

/**
 * The welcome an empty panel opens on.
 *
 * ★ WHY IT WAS REWRITTEN. The old copy said the assistant "is not connected to a model yet, so it will
 * reply with a placeholder". True in piece 1; a lie since the model was connected. Copy that describes
 * a previous version of the product is worse than no copy — the reader believes it and stops asking.
 */
describe('AssistantConversationComponent — the welcome', () => {
  let fixture: ComponentFixture<AssistantConversationComponent>;
  let store: AssistantStore;
  let translate: TranslateService;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [AssistantConversationComponent, TranslateModule.forRoot()],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: AssistantApiService, useValue: apiSpy() },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(AssistantConversationComponent);
    store = TestBed.inject(AssistantStore);
    translate = TestBed.inject(TranslateService);
  });

  /** Opens the panel on a thread with no turns — the state a new conversation starts in. */
  function openEmpty(): HTMLElement {
    store.isOpen.set(true);
    store.conversation.set(CONVERSATION);
    fixture.detectChanges();
    return fixture.nativeElement.querySelector('[data-testid="assistant-welcome"]');
  }

  it('★ renders all THREE parts: the greeting, what to ask, and the read-only promise', () => {
    const welcome = openEmpty();

    expect(welcome).toBeTruthy();
    expect(welcome.querySelector('.assistant-welcome__greeting')?.textContent)
      .toContain('WELCOME_GREETING');
    expect(welcome.querySelector('.assistant-welcome__subtitle')?.textContent)
      .toContain('WELCOME_SUBTITLE');
    expect(welcome.querySelector('[data-testid="assistant-welcome-readonly"]')?.textContent)
      .toContain('WELCOME_READ_ONLY');
  });

  it('★ no longer claims the assistant is unconnected — that copy is GONE, not hidden', () => {
    // It answers for real now. A panel that greets the user by saying it cannot answer is the kind of
    // stale copy that quietly costs a feature its users.
    const text: string = fixture.nativeElement.textContent ?? '';
    openEmpty();

    expect(text).not.toContain('EMPTY_TITLE');
    expect(text).not.toContain('EMPTY_DESC');
    expect(fixture.nativeElement.textContent).not.toContain('EMPTY_DESC');

    // And the keys themselves are retired from every language, so nothing can render them by accident.
    for (const language of ['en', 'es', 'pl']) {
      const bundle = ASSISTANT_BUNDLES[language];
      expect(bundle['EMPTY_TITLE']).withContext(`${language} still declares EMPTY_TITLE`).toBeUndefined();
      expect(bundle['EMPTY_DESC']).withContext(`${language} still declares EMPTY_DESC`).toBeUndefined();
    }
  });

  it('★ offers NOTHING the assistant cannot do', () => {
    // The visual references for a screen like this carry "Add attachment" and "Use image". This
    // assistant does neither. The style was borrowed; the promises were not — the same rule the
    // composer lives under, and the same reason: a control that does nothing is a promise the engine
    // cannot keep.
    //
    // ★ THIS TEST USED TO ASSERT ZERO BUTTONS, and the suggested prompts are why it no longer can.
    // The rule it was defending was never "no buttons" — it was "nothing offered that the engine
    // cannot do". So the count is gone and the RULE is asserted instead: every button in the welcome
    // is a starter prompt, and the companion test below ties each starter to a real server tool.
    const welcome = openEmpty();

    expect(welcome.querySelector('input')).toBeNull();
    expect(welcome.querySelector('input[type="file"]')).toBeNull();

    const buttons = Array.from(welcome.querySelectorAll('button')) as HTMLButtonElement[];
    expect(buttons.length).withContext('only the starter prompts').toBe(STARTER_PROMPTS.length);

    const text = (welcome.textContent ?? '').toLowerCase();
    for (const word of ['attach', 'image', 'upload', 'file', 'voice', 'search']) {
      expect(text).withContext(`the welcome must not mention "${word}"`).not.toContain(word);
    }
  });

  // ══ THE STARTER PROMPTS ════════════════════════════════════════════════════

  /**
   * ★★ THE LIST THIS SUITE DEFENDS. These are the tools the SERVER registers
   * (Wasnie.Infrastructure/DependencyInjection.cs) — the complete set of things the assistant can look
   * up. A starter naming anything outside it is a question the engine cannot answer, offered by the
   * product itself, and the user finds out by clicking.
   */
  const REAL_TOOLS = [
    'get_payee_ledger_summary',
    'get_payee_plans',
    'get_transaction',
    'get_plan_rules',
  ];

  it('★ every starter maps to a tool the server actually registers', () => {
    for (const starter of STARTER_PROMPTS) {
      expect(REAL_TOOLS)
        .withContext(`"${starter.labelKey}" promises ${starter.tool}, which does not exist`)
        .toContain(starter.tool);
    }
  });

  it('clicking a starter FILLS the composer and does not send', () => {
    translate.setTranslation('en', {
      ASSISTANT: { STARTER_BALANCE_PROMPT: 'What is the balance of [payee name]?' },
    });
    translate.use('en');
    openEmpty();

    const button: HTMLButtonElement =
      fixture.nativeElement.querySelector('[data-testid="assistant-starter-balance"]');
    button.click();
    fixture.detectChanges();

    const textarea: HTMLTextAreaElement =
      fixture.nativeElement.querySelector('[data-testid="assistant-composer"] textarea');

    expect(textarea.value).toBe('What is the balance of [payee name]?');
    expect(fixture.componentInstance.draft()).toBe('What is the balance of [payee name]?');

    // ★ NOTHING WAS SENT. A sentence with a hole in it, sent on click, asks for a payee literally
    // called "[payee name]" — the user's first impression would be the assistant finding nobody.
    expect(store.messages().length).toBe(0);
  });

  it('the placeholder is left SELECTED so the next keystroke replaces it', () => {
    const prompt = 'What is the balance of [payee name]?';
    translate.setTranslation('en', { ASSISTANT: { STARTER_BALANCE_PROMPT: prompt } });
    translate.use('en');
    openEmpty();

    fixture.nativeElement
      .querySelector('[data-testid="assistant-starter-balance"]')
      .click();
    fixture.detectChanges();

    const textarea: HTMLTextAreaElement =
      fixture.nativeElement.querySelector('[data-testid="assistant-composer"] textarea');

    expect(textarea.selectionStart).toBe(prompt.indexOf('['));
    expect(textarea.selectionEnd).toBe(prompt.indexOf(']') + 1);
    expect(document.activeElement).withContext('the caret is in the composer').toBe(textarea);
  });

  it('fills in the language in use, not always in English', () => {
    translate.setTranslation('es', {
      ASSISTANT: { STARTER_BALANCE_PROMPT: '¿Cuál es el balance de [nombre del payee]?' },
    });
    translate.use('es');
    openEmpty();

    fixture.nativeElement
      .querySelector('[data-testid="assistant-starter-balance"]')
      .click();
    fixture.detectChanges();

    expect(fixture.componentInstance.draft()).toBe('¿Cuál es el balance de [nombre del payee]?');
  });

  it('the starters are part of the EMPTY state and leave once the thread has turns', () => {
    expect(openEmpty().querySelector('[data-testid="assistant-starters"]')).toBeTruthy();

    store.conversation.set({
      ...CONVERSATION,
      messages: [
        {
          id: 'm1', role: 'User', content: 'hola',
          payload: null, sequence: 0, createdAt: '2026-08-12T09:00:00Z',
        },
      ],
    });
    fixture.detectChanges();

    // They live inside the welcome, so they go when it goes — and come back on a new conversation,
    // because a new conversation is an empty one.
    expect(fixture.nativeElement.querySelector('[data-testid="assistant-starters"]')).toBeNull();
  });

  it('the read-only promise carries WEIGHT, not small-print styling', () => {
    // ★ Asserted by COMPUTED style, and the assertion MOVED with the design. It used to sit in a
    // bordered box; on screen that turned a sentence into a widget, so the box went and the weight
    // stayed. What must never change is the property underneath both: this is the sentence that makes
    // the assistant safe to hand a finance team, so it may not be rendered as a faded footnote.
    const welcome = openEmpty();
    const note = welcome.querySelector('[data-testid="assistant-welcome-readonly"]') as HTMLElement;
    const styles = getComputedStyle(note);
    const subtitle = welcome.querySelector('.assistant-welcome__subtitle') as HTMLElement;

    expect(parseInt(styles.fontWeight, 10))
      .withContext('semibold is what stops it reading as small print').toBeGreaterThanOrEqual(600);

    // Shares the subtitle's left edge — one column, not an inset panel.
    expect(styles.paddingLeft).toBe(getComputedStyle(subtitle).paddingLeft);

    // Not faded into the background: the same body colour the subtitle uses, never the tertiary grey.
    expect(styles.color).toBe(getComputedStyle(subtitle).color);

    const greeting = welcome.querySelector('.assistant-welcome__greeting') as HTMLElement;
    expect(parseFloat(getComputedStyle(greeting).fontSize))
      .withContext('the greeting is the largest thing here')
      .toBeGreaterThan(parseFloat(styles.fontSize));
  });

  it('the logo sits on the LEFT, not centred in a stretched box', () => {
    // ★ The bug this catches is subtle and was real: the welcome is a flex COLUMN, so the default
    // `align-items: stretch` widened the image box to the full column and `object-fit` centred the
    // mark inside it — left-aligned CSS, centred result. Comparing the logo's left edge to the
    // greeting's is what actually notices that.
    const welcome = openEmpty();
    const logo = welcome.querySelector('.assistant-welcome__logo') as HTMLElement;
    const greeting = welcome.querySelector('.assistant-welcome__greeting') as HTMLElement;

    expect(logo).toBeTruthy();
    expect(Math.round(logo.getBoundingClientRect().left))
      .withContext('the mark lines up with the greeting under it')
      .toBe(Math.round(greeting.getBoundingClientRect().left));
  });

  it('the greeting is large enough to read as one, and fits the panel', () => {
    const welcome = openEmpty();
    const greeting = welcome.querySelector('.assistant-welcome__greeting') as HTMLElement;
    const size = parseFloat(getComputedStyle(greeting).fontSize);

    expect(size).withContext('large, with presence').toBeGreaterThanOrEqual(20);
    // ★ The ceiling moved from 28 to 32 when the greeting was deliberately enlarged — this is the new
    // intent, not a loosened assertion. It still HAS a ceiling because the panel is min(420px, 100vw)
    // and the size is a clamp: past this the Spanish greeting stops being a greeting and becomes a
    // headline wrapping onto three lines.
    expect(size).withContext('not larger than the panel can carry').toBeLessThanOrEqual(32);
  });

  it('the welcome is gone once the conversation has turns', () => {
    store.isOpen.set(true);
    store.conversation.set({
      ...CONVERSATION,
      messages: [
        { id: 'm1', role: 'User', content: 'hello', payload: null, sequence: 0, createdAt: '' },
      ],
    });
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('[data-testid="assistant-welcome"]')).toBeNull();
  });

  it('the COMPOSER was not touched', () => {
    // The WI was explicit: the welcome only. This is what notices if a later edit drifts into it.
    store.isOpen.set(true);
    store.conversation.set(CONVERSATION);
    fixture.detectChanges();

    const composer = fixture.nativeElement.querySelector('[data-testid="assistant-composer-card"]');
    expect(composer).toBeTruthy();
    expect(composer.querySelector('textarea')).toBeTruthy();
    // Still exactly one control in there: send.
    expect(composer.querySelectorAll('button').length).toBe(1);
    // And the permanent disclaimer under it is a different thing, still present.
    expect(fixture.nativeElement.querySelector('[data-testid="assistant-disclaimer"]')).toBeTruthy();
  });

  it('renders the real Spanish copy when a language is loaded', () => {
    translate.setTranslation('es', {
      ASSISTANT: {
        WELCOME_GREETING: '¿En qué puedo ayudarte con Wasnie?',
        WELCOME_READ_ONLY: 'Este asistente es de solo lectura: te orienta y responde tus preguntas, pero nunca modifica datos ni ejecuta acciones en tu sistema. Verificá la información importante.',
      },
    });
    translate.use('es');

    const welcome = openEmpty();

    expect(welcome.textContent).toContain('¿En qué puedo ayudarte con Wasnie?');
    expect(welcome.textContent).toContain('nunca modifica datos ni ejecuta acciones');
  });
});

describe('Assistant i18n — the welcome exists in every language', () => {
  const KEYS = ['WELCOME_GREETING', 'WELCOME_SUBTITLE', 'WELCOME_READ_ONLY'];

  it('★ EN, ES and PL all carry the three keys, with no English left in the others', () => {
    for (const language of ['en', 'es', 'pl']) {
      for (const key of KEYS) {
        const value = ASSISTANT_BUNDLES[language][key];
        expect(value).withContext(`${language}.ASSISTANT.${key} is missing`).toBeTruthy();
        expect((value ?? '').trim().length).toBeGreaterThan(0);
      }
    }

    // Each language says it in its own words — an untranslated key copied across is the failure mode
    // "i18n is complete" is meant to prevent, and it passes a mere presence check.
    for (const key of KEYS) {
      expect(ASSISTANT_BUNDLES['es'][key]).not.toBe(ASSISTANT_BUNDLES['en'][key]);
      expect(ASSISTANT_BUNDLES['pl'][key]).not.toBe(ASSISTANT_BUNDLES['en'][key]);
    }
  });

  it('the read-only promise is stated in every language, not only in English', () => {
    // The guarantee is the point of the note; a language that lost it would be shipping a different
    // promise to its readers.
    expect(ASSISTANT_BUNDLES['en']['WELCOME_READ_ONLY']).toContain('read-only');
    expect(ASSISTANT_BUNDLES['es']['WELCOME_READ_ONLY']).toContain('solo lectura');
    expect(ASSISTANT_BUNDLES['pl']['WELCOME_READ_ONLY']).toContain('tylko do odczytu');
  });

  it('★ every starter has a label AND a prompt in EN, ES and PL', () => {
    for (const language of ['en', 'es', 'pl']) {
      for (const starter of STARTER_PROMPTS) {
        for (const key of [starter.labelKey, starter.promptKey]) {
          const bare = key.replace('ASSISTANT.', '');
          expect(ASSISTANT_BUNDLES[language][bare])
            .withContext(`${language} is missing ${bare}`)
            .toBeTruthy();
        }
      }
    }
  });

  it('the filled-in sentence is real in each language, not English left behind', () => {
    for (const starter of STARTER_PROMPTS) {
      const bare = starter.promptKey.replace('ASSISTANT.', '');

      // A key copied across from English passes a presence check and fails the user.
      expect(ASSISTANT_BUNDLES['es'][bare]).not.toBe(ASSISTANT_BUNDLES['en'][bare]);
      expect(ASSISTANT_BUNDLES['pl'][bare]).not.toBe(ASSISTANT_BUNDLES['en'][bare]);
    }
  });

  it('★ every translated sentence keeps the placeholder the composer selects', () => {
    // The selection is derived from the brackets rather than from a per-language offset, so a
    // translation that dropped them would silently leave the user with a caret at the end and a
    // literal "[payee name]" to delete by hand.
    for (const language of ['en', 'es', 'pl']) {
      for (const starter of STARTER_PROMPTS) {
        const sentence = ASSISTANT_BUNDLES[language][starter.promptKey.replace('ASSISTANT.', '')];
        const range = placeholderRange(sentence);

        expect(range.end)
          .withContext(`${language} ${starter.promptKey} lost its [placeholder]`)
          .toBeGreaterThan(range.start);
      }
    }
  });
});

describe('placeholderRange — which part of the sentence gets selected', () => {
  it('selects the bracketed placeholder, brackets included', () => {
    const text = 'What is the balance of [payee name]?';

    expect(placeholderRange(text)).toEqual({ start: 23, end: 35 });
    expect(text.slice(23, 35)).toBe('[payee name]');
  });

  it('puts the caret at the END when there is no placeholder', () => {
    // Not {0,0}: selecting nothing at position zero would put the caret before the sentence, and the
    // user's first keystroke would land in front of the question instead of after it.
    expect(placeholderRange('Hola')).toEqual({ start: 4, end: 4 });
  });

  it('ignores an unclosed bracket rather than selecting the rest of the sentence', () => {
    expect(placeholderRange('balance of [payee')).toEqual({ start: 17, end: 17 });
  });
});

/**
 * The panel's exit.
 *
 * ★ WHY THIS ONE GETS TESTS WHEN THE ENTRANCE DID NOT. The entrance is a keyframe — nothing to assert
 * but the existence of a class. The exit is a small state machine: the panel must OUTLIVE the click
 * that closed it, and then actually close. Every branch here can fail as "the panel will not close",
 * which is not a cosmetic bug.
 */
describe('AssistantPanelComponent — the closing animation', () => {
  let fixture: ComponentFixture<AssistantPanelComponent>;
  let component: AssistantPanelComponent;
  let store: AssistantStore;

  /** The name in the stylesheet. If it is renamed there and not here, the panel stops closing. */
  const EXIT = 'assistant-panel-out';

  function reducedMotion(enabled: boolean): void {
    spyOn(window, 'matchMedia').and.callFake((query: string) => ({
      matches: enabled && query.includes('prefers-reduced-motion'),
      media: query,
    }) as MediaQueryList);
  }

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [AssistantPanelComponent, TranslateModule.forRoot()],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: AssistantApiService, useValue: apiSpy() },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(AssistantPanelComponent);
    component = fixture.componentInstance;
    store = TestBed.inject(AssistantStore);
    store.isOpen.set(true);
    store.conversation.set(CONVERSATION);
    fixture.detectChanges();
  });

  it('★ the ELEMENT outlives the click, but the STATE closes immediately', () => {
    // ★ THE REGRESSION THIS FILE EXISTS FOR. The first version kept `isOpen` true until the animation
    // finished, so for 200ms the application still believed the assistant was open — and anything that
    // re-rendered the panel in that window brought it back at full opacity. On screen that read as
    // "the chat opens whenever I click a sidebar item"; it had never closed.
    reducedMotion(false);

    component.close();
    fixture.detectChanges();

    expect(store.isOpen()).withContext('the state closes at once, not when the animation ends').toBeFalse();
    expect(component.closing()).withContext('only the element lingers').toBeTrue();
    expect(fixture.nativeElement.querySelector('[data-testid="assistant-panel"]')).toBeTruthy();

    // ...and while it leaves it is neither a dialog nor a click target.
    const panel: HTMLElement = fixture.nativeElement.querySelector('[data-testid="assistant-panel"]');
    expect(panel.classList).toContain('assistant-panel--closing');
    expect(panel.getAttribute('aria-hidden')).toBe('true');
    expect(panel.getAttribute('aria-modal')).toBeNull();
  });

  it('★ a re-render mid-exit does NOT bring the panel back', () => {
    // The bug reproduced directly: close, then force the change detection a navigation would cause.
    // With the state already closed there is nothing for a re-render to restore.
    reducedMotion(false);
    component.close();

    fixture.detectChanges();
    fixture.detectChanges();

    expect(store.isOpen()).toBeFalse();
    const panel: HTMLElement = fixture.nativeElement.querySelector('[data-testid="assistant-panel"]');
    expect(panel?.classList).withContext('still on its way out, never restored').toContain('assistant-panel--closing');
  });

  it('★ a FRESH panel component does not show a closed assistant', () => {
    // ★ What actually put the chat back on screen: the shell re-creating the panel while `isOpen` was
    // still true. A new instance starts with `closing` false, so the old design rendered it wide open.
    reducedMotion(false);
    component.close();

    const second = TestBed.createComponent(AssistantPanelComponent);
    second.detectChanges();

    expect(second.nativeElement.querySelector('[data-testid="assistant-panel"]'))
      .withContext('a new instance must agree the assistant is closed').toBeNull();
  });

  it('reopening while it leaves cancels the exit', () => {
    reducedMotion(false);
    component.close();
    expect(component.closing()).toBeTrue();

    store.isOpen.set(true);
    fixture.detectChanges();

    expect(component.closing()).withContext('it is being shown, not sent away').toBeFalse();
    const panel: HTMLElement = fixture.nativeElement.querySelector('[data-testid="assistant-panel"]');
    expect(panel.classList).not.toContain('assistant-panel--closing');
    expect(panel.getAttribute('aria-modal')).toBe('true');
  });

  it('★ the element is removed once the exit finishes', () => {
    reducedMotion(false);
    component.close();

    component.onCloseAnimationEnd(new AnimationEvent('animationend', { animationName: EXIT }));
    fixture.detectChanges();

    expect(store.isOpen()).toBeFalse();
    expect(component.closing()).toBeFalse();
    expect(fixture.nativeElement.querySelector('[data-testid="assistant-panel"]')).toBeNull();
  });

  it('a child animation finishing does NOT close the panel', () => {
    // ★ `animationend` BUBBLES, and this panel contains other animations — the step spinner among
    // them. Closing on "an animation ended somewhere in here" would make the panel shut itself while
    // the assistant was still working.
    reducedMotion(false);
    component.close();

    component.onCloseAnimationEnd(
      new AnimationEvent('animationend', { animationName: 'assistant-step-spin' }));
    fixture.detectChanges();

    expect(component.closing())
      .withContext('a step spinner must not rip the panel out mid-animation').toBeTrue();
    expect(fixture.nativeElement.querySelector('[data-testid="assistant-panel"]')).toBeTruthy();
  });

  it('★ closes IMMEDIATELY when the user asked for reduced motion', () => {
    // ★ THE BRANCH THAT WOULD HAVE BROKEN IT. With `animation: none` the `animationend` event never
    // fires, so waiting for it would leave the panel open forever — for exactly the people who asked
    // for less movement. An accessibility preference must not become a broken close button.
    reducedMotion(true);

    component.close();
    fixture.detectChanges();

    expect(store.isOpen()).toBeFalse();
    expect(component.closing()).toBeFalse();
  });

  it('a second click while it is already leaving changes nothing', () => {
    reducedMotion(false);

    component.close();
    component.close();

    expect(store.isOpen()).toBeFalse();
    expect(component.closing()).toBeTrue();

    component.onCloseAnimationEnd(new AnimationEvent('animationend', { animationName: EXIT }));
    expect(component.closing()).toBeFalse();
  });

  it('the backdrop leaves with the panel, not after it', () => {
    reducedMotion(false);
    component.close();
    fixture.detectChanges();

    const backdrop: HTMLElement = fixture.nativeElement.querySelector('[data-testid="assistant-backdrop"]');
    expect(backdrop.classList).toContain('assistant-backdrop--closing');
  });
});

/**
 * The waiting message.
 *
 * ★ THE COPY ITSELF NEEDS NO TEST — it is a translated string next to three dots. The CLOCK does: it
 * starts, it must be cancelled when the answer arrives, and it must not outlive the panel. Each of
 * those failing looks like "the assistant says it is still working after it answered".
 */
describe('AssistantConversationComponent — the waiting message', () => {
  let fixture: ComponentFixture<AssistantConversationComponent>;
  let component: AssistantConversationComponent;
  let store: AssistantStore;

  /** Comfortably past the threshold; the exact value lives in the component, not here. */
  const PAST_THRESHOLD = 6000;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [AssistantConversationComponent, TranslateModule.forRoot()],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: AssistantApiService, useValue: apiSpy() },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(AssistantConversationComponent);
    component = fixture.componentInstance;
    store = TestBed.inject(AssistantStore);
    store.isOpen.set(true);
    store.conversation.set(CONVERSATION);
  });

  /** The state between "the request went out" and "the first token came back". */
  function startWaiting(): void {
    store.setStreamState(store.activeDraftKey(), { reply: '' });
    fixture.detectChanges();
  }

  it('shows the honest base message beside the assistant mark, immediately', () => {
    startWaiting();

    const waiting: HTMLElement = fixture.nativeElement.querySelector('[data-testid="assistant-waiting"]');
    expect(waiting).toBeTruthy();
    expect(waiting.textContent).toContain('PROCESSING');
    // ★ The mark is ACCOMPANIED, not replaced. (It used to be three dots; it is now the avatar.)
    expect(waiting.querySelector('.assistant-msg__avatar')).toBeTruthy();
    // The long-wait line has not earned its place yet.
    expect(fixture.nativeElement.querySelector('[data-testid="assistant-waiting-long"]')).toBeNull();
  });

  it('★ explains the wait only once it has actually been a long one', fakeAsync(() => {
    startWaiting();
    expect(component.waitingLong()).toBeFalse();

    tick(PAST_THRESHOLD);
    fixture.detectChanges();

    expect(component.waitingLong()).toBeTrue();
    expect(fixture.nativeElement.querySelector('[data-testid="assistant-waiting-long"]').textContent)
      .toContain('PROCESSING_LONG');
  }));

  it('★ a fast answer never triggers it, and the clock is cancelled', fakeAsync(() => {
    startWaiting();

    // The first token arrives well inside the threshold.
    store.setStreamState(store.activeDraftKey(), { reply: 'It was ' });
    fixture.detectChanges();

    // ...and the pending timer must not fire into a turn that is already answering.
    tick(PAST_THRESHOLD);
    fixture.detectChanges();

    expect(component.waitingLong()).withContext('the wait ended; the explanation is moot').toBeFalse();
    expect(fixture.nativeElement.querySelector('[data-testid="assistant-waiting"]')).toBeNull();
  }));

  it('the explanation is taken back down for the NEXT question', fakeAsync(() => {
    startWaiting();
    tick(PAST_THRESHOLD);
    expect(component.waitingLong()).toBeTrue();

    // Answer lands, then a second question goes out.
    store.setStreamState(store.activeDraftKey(), { reply: 'done' });
    fixture.detectChanges();
    expect(component.waitingLong()).toBeFalse();

    startWaiting();
    expect(component.waitingLong())
      .withContext('a fresh wait starts from scratch, not from the last one').toBeFalse();

    tick(PAST_THRESHOLD);
    expect(component.waitingLong()).toBeTrue();
  }));

  it('the clock does not outlive the panel', fakeAsync(() => {
    startWaiting();
    fixture.destroy();

    // A timer left running would fire into a destroyed component. Nothing should be pending at all —
    // fakeAsync fails the test if a task is still queued when it ends.
    tick(PAST_THRESHOLD);

    expect(component.waitingLong()).toBeFalse();
  }));
});

/**
 * STOPPING an answer that is being written.
 *
 * ★ WHAT THESE PIN. Stop is not Send in a different costume, and a cancellation is not a failure: the
 * words that arrived are KEPT and marked, the thread is not reported as waiting on anything, and the
 * composer is usable the instant the button is pressed — which is the entire reason anyone presses it.
 */
describe('AssistantStore — stopping an answer', () => {
  let store: AssistantStore;
  let api: jasmine.SpyObj<AssistantApiService>;

  /** The turn the SERVER stores for a stopped answer: the partial text, marked. */
  const CANCELLED_ROW: AssistantMessage = {
    id: 'm-bot', role: 'Assistant', content: 'A real', payload: null, sequence: 1,
    createdAt: '2026-08-06T09:00:00Z', status: 'Cancelled',
  };

  /**
   * Frames, and then a stream that never ends on its own — exactly what the browser is holding when
   * the user reaches for Stop. It ends the only way that one does: the signal fires.
   */
  async function* untilAborted(
    list: AssistantStreamEvent[], signal: AbortSignal | undefined,
  ): AsyncGenerator<AssistantStreamEvent> {
    for (const frame of list) {
      yield frame;
    }

    await new Promise<void>((_resolve, reject) => {
      const fail = () => reject(new DOMException('Aborted', 'AbortError'));
      // No signal reached the API — the test would hang forever, so fail it loudly instead.
      if (!signal || signal.aborted) {
        fail();
        return;
      }
      signal.addEventListener('abort', fail);
    });
  }

  /** Lets the pending microtasks run so the frames already yielded reach the store. */
  const settle = () => new Promise((resolve) => setTimeout(resolve, 0));

  /** The conversation as the server holds it once the stopped answer has been written. */
  const withCancelledRow = () => ({
    ...CONVERSATION,
    messages: [exchange('a long question').userMessage, CANCELLED_ROW],
  });

  beforeEach(() => {
    api = apiSpy();
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
   * Sends a question and stops with a fragment already on screen.
   *
   * ★ THE SEND IN FLIGHT COMES BACK WRAPPED, and it has to. Returning it bare from an async function
   * makes it a promise-of-a-promise, and `await` collapses those: the caller would sit waiting for the
   * very request this helper exists to leave hanging.
   */
  async function sendAndReachHalfway(): Promise<{ sending: Promise<void> }> {
    api.streamMessage.and.callFake((
      _id: string, content: string, _token: string | null, signal?: AbortSignal) =>
      untilAborted([
        { type: 'user', message: exchange(content).userMessage },
        { type: 'delta', delta: 'A real ' },
      ], signal));

    await store.startConversation();
    const sending = store.send('a long question');
    await settle();
    return { sending };
  }

  it('★ keeps the words that arrived, as the row the SERVER stored for them', async () => {
    const { sending } = await sendAndReachHalfway();

    expect(store.sending()).withContext('the answer is still being written').toBeTrue();
    expect(store.streamingReply()).toBe('A real ');

    api.getConversation.and.returnValue(of(withCancelledRow()));

    await store.cancel();
    await sending;

    // The partial answer is a STORED turn now, not a bubble this browser is remembering.
    expect(store.messages().length).toBe(2);
    expect(store.messages()[1].status).toBe('Cancelled');
    expect(store.messages()[1].content).toBe('A real');
    expect(store.streamingReply()).withContext('the stored row replaced it').toBeNull();
  });

  it('★ shows the cancelled turn AT THE CLICK, before the server has been asked anything', async () => {
    const { sending } = await sendAndReachHalfway();

    // ★ THE BUG THIS PINS. The first version froze the partial and waited for the stored row to read
    // back — and that read loses the race, because the server writes it while the aborted request
    // unwinds. Both asks returned a thread with no cancelled turn, the frozen text was cleared as
    // "it never existed", and the answer VANISHED on the click meant to preserve it. Only a refresh
    // brought it back.
    //
    // The server never answers in this test: `getConversation` hangs forever, which is the strongest
    // possible version of losing that race.
    api.getConversation.and.returnValue(NEVER);

    const cancelling = store.cancel();
    await settle();

    expect(store.messages().length).withContext('the stopped answer is in the thread').toBe(2);
    expect(store.messages()[1].status).toBe('Cancelled');
    expect(store.messages()[1].content).toBe('A real');
    expect(store.streamingReply()).withContext('no bubble left mid-stream').toBeNull();
    expect(store.sending()).withContext('the composer is free at the click').toBeFalse();

    // And it is not taken away again when the server never confirms it.
    expect(store.messages().length).toBe(2);

    await sending;
    void cancelling;
  });

  it('replaces the stand-in with the server row once it lands', async () => {
    const { sending } = await sendAndReachHalfway();
    api.getConversation.and.returnValue(of(withCancelledRow()));

    await store.cancel();
    await sending;

    // Same words, but now the authoritative row: the server's id, not the local placeholder.
    expect(store.messages()[1].id).toBe('m-bot');
    expect(store.messages()[1].id).not.toBe('pending-cancelled');
    expect(store.messages()[1].status).toBe('Cancelled');
  });

  it('★ is not a failure: no error, no retry offered', async () => {
    const { sending } = await sendAndReachHalfway();
    api.getConversation.and.returnValue(of(withCancelledRow()));

    await store.cancel();
    await sending;

    // Aborting the fetch throws exactly like a dead connection. Telling the user the assistant could
    // not answer would blame them for a fault, and offer to re-run a turn they deliberately ended.
    expect(store.errorKey()).toBeNull();
    expect(store.retryable()).toBeNull();
  });

  it('★ frees the composer immediately — that is the whole point of the button', async () => {
    const { sending } = await sendAndReachHalfway();
    api.getConversation.and.returnValue(of(withCancelledRow()));

    await store.cancel();
    await sending;

    expect(store.sending()).toBeFalse();
    expect(store.progressSteps()).toEqual([]);

    // And a new question really does go out, rather than being swallowed by the guard on `sending`.
    api.streamMessage.and.callFake((_id: string, content: string) =>
      frames([
        { type: 'user', message: exchange(content).userMessage },
        { type: 'done', message: { ...exchange(content).assistantMessage, content: 'Another answer.' } },
      ]));

    await store.send('something else entirely');

    expect(api.streamMessage.calls.mostRecent().args[1]).toBe('something else entirely');
  });

  it('stores nothing when it is stopped before the first word', async () => {
    api.streamMessage.and.callFake((
      _id: string, content: string, _token: string | null, signal?: AbortSignal) =>
      untilAborted([{ type: 'user', message: exchange(content).userMessage }], signal));

    await store.startConversation();
    const sending = store.send('a long question');
    await settle();

    api.getConversation.calls.reset();
    await store.cancel();
    await sending;

    // Nothing was written, so there is no row to read back — an empty bubble is not a shorter answer.
    expect(api.getConversation).not.toHaveBeenCalled();
    expect(store.streamingReply()).toBeNull();
    expect(store.errorKey()).toBeNull();
  });

  it('does nothing at all when there is no answer in flight', async () => {
    await store.startConversation();

    await store.cancel();

    expect(store.sending()).toBeFalse();
    expect(store.errorKey()).toBeNull();
  });

  it('★ hands the abort signal to the API — that is what stops the model generating', async () => {
    const { sending } = await sendAndReachHalfway();

    const signal = api.streamMessage.calls.mostRecent().args[3] as AbortSignal;
    expect(signal).withContext('without it there is nothing to abort').toBeTruthy();
    expect(signal.aborted).toBeFalse();

    api.getConversation.and.returnValue(of(withCancelledRow()));

    await store.cancel();
    await sending;

    // The server sees this as the connection going: it drops the call to the model — no more tokens
    // paid for words nobody will read — and writes what had arrived.
    expect(signal.aborted).toBeTrue();
  });
});

describe('AssistantPanelComponent — the stop button and the cancelled turn', () => {
  let fixture: ComponentFixture<AssistantPanelComponent>;
  let store: AssistantStore;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [AssistantPanelComponent, TranslateModule.forRoot()],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: AssistantApiService, useValue: apiSpy() },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(AssistantPanelComponent);
    store = TestBed.inject(AssistantStore);
    store.isOpen.set(true);
    store.conversation.set(CONVERSATION);
  });

  const stop = () => fixture.nativeElement.querySelector('[data-testid="assistant-cancel"]');
  const send = () => fixture.nativeElement.querySelector('[data-testid="assistant-send"]');

  /** Renders a thread whose last answer carries the given status, as the server would return it. */
  function renderReply(status?: 'Complete' | 'Cancelled'): void {
    store.conversation.set({
      ...CONVERSATION,
      messages: [
        {
          id: 'm1', role: 'User', content: 'explain accelerators',
          payload: null, sequence: 0, createdAt: '',
        },
        {
          id: 'm2', role: 'Assistant', content: 'Accelerators pay', payload: null, sequence: 1,
          createdAt: '', status,
        },
      ],
    });
    fixture.detectChanges();
  }

  const notice = () =>
    fixture.nativeElement.querySelector('[data-testid="assistant-cancelled-notice"]');

  it('★ appears only while an answer is being written, beside a send button that never changes', () => {
    fixture.detectChanges();
    expect(stop()).withContext('nothing to stop').toBeNull();
    expect(send()).withContext('send is always there').toBeTruthy();

    store.setStreamState(store.activeDraftKey(), { sending: true });
    fixture.detectChanges();

    expect(stop()).withContext('an answer is in flight').toBeTruthy();
    expect(send()).withContext('send is NOT transformed into stop — they are opposites').toBeTruthy();

    store.setStreamState(store.activeDraftKey(), { sending: false });
    fixture.detectChanges();

    expect(stop()).withContext('it leaves when the answer does').toBeNull();
  });

  it('is hidden rather than shown disabled', () => {
    // A disabled control is a promise about a moment that has not arrived. There is nothing to stop.
    fixture.detectChanges();

    expect(stop()).toBeNull();
    expect(fixture.nativeElement.querySelector('[data-testid="assistant-cancel"][disabled]')).toBeNull();
  });

  it('asks the store to stop when it is clicked', () => {
    const cancel = spyOn(store, 'cancel').and.resolveTo();
    store.setStreamState(store.activeDraftKey(), { sending: true });
    fixture.detectChanges();

    (stop().querySelector('button') as HTMLElement | null)?.click();

    expect(cancel).toHaveBeenCalled();
  });

  it('★ marks a cancelled turn from the STORED row, so a reload still shows it', () => {
    // Nothing in this test ever pressed the button: the conversation arrives from the server exactly
    // as it would after a refresh, and the notice must be there anyway.
    renderReply('Cancelled');

    expect(notice()).withContext('an answer that stops mid-sentence must say who ended it').toBeTruthy();
    expect(notice().textContent).toContain('CANCELLED_NOTICE');

    // The words the user watched arrive are still on screen — cancelling does not delete the turn.
    expect(fixture.nativeElement.textContent).toContain('Accelerators pay');

    // And it is NOT the failure card: there is nothing here to retry.
    expect(fixture.nativeElement.querySelector('[data-testid="assistant-failed-alert"]')).toBeNull();
  });

  it('leaves a completed answer unmarked', () => {
    renderReply('Complete');

    expect(notice()).toBeNull();
  });

  it('treats a row from a backend that predates the field as complete', () => {
    // Every turn written before cancelling existed did run to its end; marking them all as stopped
    // would rewrite the history of the whole product.
    renderReply(undefined);

    expect(notice()).toBeNull();
  });

  it('the notice uses design system tokens, not invented values', () => {
    renderReply('Cancelled');

    const tertiary = getComputedStyle(document.documentElement)
      .getPropertyValue('--color-text-tertiary').trim();
    expect(tertiary).withContext('the token must actually be defined').not.toBe('');

    const styles = getComputedStyle(notice());
    expect(parseFloat(styles.fontSize)).toBeGreaterThan(0);
    expect(parseFloat(styles.borderTopWidth)).toBeGreaterThan(0);
  });
});

/**
 * TRY AGAIN, on an answer the user stopped.
 *
 * ★ IT IS THE FAILURE CARD'S RETRY, NOT A SECOND ONE. Same button, same `store.retry()`, same `isRetry`
 * request — so the question is re-answered rather than typed into the thread twice. What is different is
 * only where it appears and what it sits under.
 */
describe('AssistantStore — try again after a stopped answer', () => {
  let store: AssistantStore;
  let api: jasmine.SpyObj<AssistantApiService>;

  const QUESTION: AssistantMessage = {
    id: 'm1', role: 'User', content: 'explain accelerators', payload: null, sequence: 0, createdAt: '',
  };

  const STOPPED: AssistantMessage = {
    id: 'm2', role: 'Assistant', content: 'Accelerators pay', payload: null, sequence: 1,
    createdAt: '', status: 'Cancelled',
  };

  beforeEach(() => {
    api = apiSpy();
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: AssistantApiService, useValue: api },
      ],
    });
    store = TestBed.inject(AssistantStore);
    store.conversation.set({ ...CONVERSATION, messages: [QUESTION, STOPPED] });
  });

  it('★ re-answers the stored question instead of sending it again', async () => {
    api.streamMessage.and.callFake(() =>
      frames([
        { type: 'delta', delta: 'Accelerators pay above quota.' },
        {
          type: 'done',
          message: {
            id: 'm3', role: 'Assistant', content: 'Accelerators pay above quota.', payload: null,
            sequence: 2, createdAt: '', status: 'Complete',
          },
        },
      ]));

    await store.retry();

    // ★ isRetry — the flag that stops the thread duplicating the user's own words. There is no `user`
    // frame in the replay above for exactly that reason: the server has nothing new to echo.
    expect(api.streamMessage.calls.mostRecent().args[4]).toBeTrue();
    expect(api.streamMessage.calls.mostRecent().args[1]).toBe('explain accelerators');

    // The stopped fragment survives, and the fresh answer lands after it.
    expect(store.messages().length).toBe(3);
    expect(store.messages()[1].status).toBe('Cancelled');
    expect(store.messages()[2].content).toBe('Accelerators pay above quota.');
  });

  it('offers the retry only while the stopped answer is the LAST turn', () => {
    expect(store.retryableCancelled()).not.toBeNull();
    expect(store.retryableCancelled()!.content).toBe('explain accelerators');

    // Asked past: retrying re-answers the LAST question, so an offer here would re-answer a different
    // message than the one it sits beside.
    store.conversation.set({
      ...CONVERSATION,
      messages: [
        QUESTION,
        STOPPED,
        { id: 'm3', role: 'User', content: 'something else', payload: null, sequence: 2, createdAt: '' },
        {
          id: 'm4', role: 'Assistant', content: 'An answer.', payload: null, sequence: 3,
          createdAt: '', status: 'Complete',
        },
      ],
    });

    expect(store.retryableCancelled()).toBeNull();
  });

  it('offers nothing while an answer is already in flight', () => {
    store.setStreamState(store.activeDraftKey(), { sending: true });

    expect(store.retryableCancelled()).toBeNull();
  });

  it('★ does NOT raise the failure card — a stopped turn is not a failed one', () => {
    // The two offers must never both be live: `retryable` needs the thread to be waiting on an answer,
    // and a cancelled reply IS a stored answer.
    expect(store.retryable()).toBeNull();
    expect(store.retryableCancelled()).not.toBeNull();
  });
});

describe('AssistantPanelComponent — try again beside the cancelled notice', () => {
  let fixture: ComponentFixture<AssistantPanelComponent>;
  let store: AssistantStore;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [AssistantPanelComponent, TranslateModule.forRoot()],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: AssistantApiService, useValue: apiSpy() },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(AssistantPanelComponent);
    store = TestBed.inject(AssistantStore);
    store.isOpen.set(true);
  });

  const retryButton = () =>
    fixture.nativeElement.querySelector('[data-testid="assistant-cancelled-retry"]');

  function renderStoppedTurn(trailing: AssistantMessage[] = []): void {
    store.conversation.set({
      ...CONVERSATION,
      messages: [
        {
          id: 'm1', role: 'User', content: 'explain accelerators', payload: null, sequence: 0,
          createdAt: '',
        },
        {
          id: 'm2', role: 'Assistant', content: 'Accelerators pay', payload: null, sequence: 1,
          createdAt: '', status: 'Cancelled',
        },
        ...trailing,
      ],
    });
    fixture.detectChanges();
  }

  it('★ sits inside the notice, to the right of it', () => {
    renderStoppedTurn();

    const notice = fixture.nativeElement.querySelector('[data-testid="assistant-cancelled-notice"]');

    expect(retryButton()).withContext('a stopped answer needs a way back to a real one').toBeTruthy();
    expect(notice.contains(retryButton()))
      .withContext('the way out belongs to the note that explains the state').toBeTrue();
    expect(notice.textContent).toContain('RETRY');
  });

  it('runs the SAME retry as the failure card', () => {
    const retry = spyOn(store, 'retry').and.resolveTo();
    renderStoppedTurn();

    (retryButton().querySelector('button') as HTMLElement | null)?.click();

    expect(retry).toHaveBeenCalled();
  });

  it('is absent on an older cancelled turn the user has asked past', () => {
    renderStoppedTurn([
      { id: 'm3', role: 'User', content: 'something else', payload: null, sequence: 2, createdAt: '' },
      {
        id: 'm4', role: 'Assistant', content: 'An answer.', payload: null, sequence: 3,
        createdAt: '', status: 'Complete',
      },
    ]);

    // The notice stays — that turn WAS cancelled, and that does not stop being true.
    expect(fixture.nativeElement.querySelector('[data-testid="assistant-cancelled-notice"]')).toBeTruthy();
    expect(retryButton()).withContext('it would re-answer a different question').toBeNull();
  });

  it('disappears while the retry it started is running', () => {
    renderStoppedTurn();
    expect(retryButton()).toBeTruthy();

    store.setStreamState(store.activeDraftKey(), { sending: true });
    fixture.detectChanges();

    // Otherwise it sits there inviting a second retry of the request already in flight.
    expect(retryButton()).toBeNull();
  });
});

describe('Assistant i18n — stopping an answer speaks every language', () => {
  // ★ RETRY IS THE FAILURE CARD'S OWN KEY, REUSED. The stopped turn's button says the same thing for
  // the same reason, and inventing a second key would be two strings to keep saying it identically in
  // three languages. It is asserted here because this notice now depends on it too.
  const KEYS = ['CANCEL', 'CANCELLED_NOTICE', 'RETRY'];

  it('★ EN, ES and PL all carry every key, each in its own words', () => {
    for (const language of ['en', 'es', 'pl']) {
      for (const key of KEYS) {
        const value = ASSISTANT_BUNDLES[language][key];
        expect(value).withContext(`${language}.ASSISTANT.${key} is missing`).toBeTruthy();
        expect((value ?? '').trim().length).toBeGreaterThan(0);
      }
    }

    for (const key of KEYS) {
      expect(ASSISTANT_BUNDLES['es'][key]).not.toBe(ASSISTANT_BUNDLES['en'][key]);
      expect(ASSISTANT_BUNDLES['pl'][key]).not.toBe(ASSISTANT_BUNDLES['en'][key]);
    }
  });
});
