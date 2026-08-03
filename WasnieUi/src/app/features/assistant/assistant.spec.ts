import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ApplicationRef } from '@angular/core';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { Router, provideRouter } from '@angular/router';
import { TranslateModule, TranslateService } from '@ngx-translate/core';
import { of, throwError } from 'rxjs';
import { AssistantStore } from './state/assistant.store';
import { AssistantApiService } from './services/assistant.api.service';
import { AssistantPanelComponent } from './panel/assistant-panel.component';
import { AssistantTriggerComponent } from './trigger/assistant-trigger.component';
import {
  ASSISTANT_NOT_CONNECTED,
  AssistantConversation,
  AssistantExchange,
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
  api.getEntitlement.and.returnValue(of({ enabled: true }));
  api.listConversations.and.returnValue(of([]));
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
    store.errorKey.set('ASSISTANT.ERROR_UNAVAILABLE');

    await store.startConversation();
    await store.send('trying again');

    expect(store.errorKey()).toBeNull();
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

  async function setup(enabled: boolean) {
    api = apiSpy();
    api.getEntitlement.and.returnValue(of({ enabled }));

    await TestBed.configureTestingModule({
      imports: [AssistantTriggerComponent, TranslateModule.forRoot()],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
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

  function scrollSpy() {
    return spyOn(fixture.componentInstance, 'scrollToBottom');
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

  it('shows a real title as it was stored', () => {
    store.isOpen.set(true);
    store.conversation.set({ ...CONVERSATION, title: '¿Cómo creo un plan?' });
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('.assistant-panel__title').textContent)
      .toContain('¿Cómo creo un plan?');
  });

  it('keeps title, meta and delete in every history row after the compacting', () => {
    // The row was compacted (padding/gap) so more threads fit on screen. Spacing is Rodolfo's call, but
    // the three parts of the row are not — this is what catches a "compact" that dropped one of them.
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
      expect(row.querySelector('.assistant-history__item-title')).toBeTruthy();
      expect(row.querySelector('.assistant-history__item-meta')).toBeTruthy();
      expect(row.querySelector('.assistant-history__item-delete')).toBeTruthy();
    }

    expect(rows[0].querySelector('.assistant-history__item-title').textContent).toContain('Primera');
    expect(rows[0].querySelector('.assistant-history__item-meta').textContent).toContain('4');
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
