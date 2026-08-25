/**
 * The full-page assistant, its URL, and the drawer's Expand button.
 *
 * ★ WHAT IS TESTED HERE IS THE WIRING, NOT THE PICTURE. Render and animation are not tested in this
 * project; what these pin is the part that silently breaks: which conversation the URL loads, whether
 * a bad id is caught instead of showing somebody the wrong thread, and that Expand carries the id.
 */
import { ApplicationRef } from '@angular/core';
import { ComponentFixture, TestBed, fakeAsync, tick } from '@angular/core/testing';
import { ActivatedRoute, Router } from '@angular/router';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TranslateModule } from '@ngx-translate/core';
import { BehaviorSubject, of } from 'rxjs';
import { convertToParamMap, ParamMap } from '@angular/router';

import { AssistantPageComponent } from './page/assistant-page.component';
import { AssistantPanelComponent } from './panel/assistant-panel.component';
import { AssistantConversationListComponent } from './conversation-list/assistant-conversation-list.component';
import { AssistantConversationComponent } from './conversation/assistant-conversation.component';
import { AssistantStore } from './state/assistant.store';
import { AssistantApiService } from './services/assistant.api.service';
import { AssistantConversation } from './models/assistant.model';

const conversation = (id: string): AssistantConversation => ({
  id,
  title: 'A thread',
  createdAt: '',
  updatedAt: '',
  messages: [],
} as unknown as AssistantConversation);

describe('AssistantPageComponent — the URL is the conversation', () => {
  let fixture: ComponentFixture<AssistantPageComponent>;
  let store: AssistantStore;
  let router: jasmine.SpyObj<Router>;
  let params: BehaviorSubject<ParamMap>;

  async function mountWith(id: string | null): Promise<void> {
    params = new BehaviorSubject<ParamMap>(convertToParamMap(id ? { conversationId: id } : {}));

    await TestBed.configureTestingModule({
      imports: [AssistantPageComponent, TranslateModule.forRoot()],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: AssistantApiService, useValue: {} },
        { provide: ActivatedRoute, useValue: { paramMap: params.asObservable(), queryParams: of({}) } },
        { provide: Router, useValue: jasmine.createSpyObj('Router', ['navigate', 'navigateByUrl', 'createUrlTree', 'serializeUrl']) },
      ],
    });

    // ★ The template is stubbed on purpose, and BEFORE anything is injected — overriding after the
    // module is instantiated throws. Every assertion here is about the component's own wiring: which
    // id it loads, what it reports, where it navigates. Rendering the real template pulls in app-shell
    // and with it the sidebar, the topbar and the session timers — none of it under test, all of it
    // needing routing doubles this suite would then have to keep in step.
    TestBed.overrideComponent(AssistantPageComponent, { set: { template: '<div></div>', imports: [] } });
    await TestBed.compileComponents();

    store = TestBed.inject(AssistantStore);
    router = TestBed.inject(Router) as jasmine.SpyObj<Router>;
    router.navigate.and.resolveTo(true);

    spyOn(store, 'loadEntitlement').and.resolveTo();
    spyOn(store, 'loadConversations').and.resolveTo();
    store.entitled.set(true);

    fixture = TestBed.createComponent(AssistantPageComponent);
  }

  it('loads the conversation named in the URL', fakeAsync(async () => {
    await mountWith('c-42');
    const open = spyOn(store, 'openConversation').and.callFake(async (id: string) => {
      store.conversation.set(conversation(id));
    });

    fixture.detectChanges();
    tick();

    expect(open).toHaveBeenCalledWith('c-42');
    expect(fixture.componentInstance.notFound()).toBeFalse();
  }));

  // ★ The check that makes /assistant/{id} honest. openConversation leaves the previous conversation
  // in place when the request fails, so without comparing ids afterwards a link to someone else's
  // thread would silently show the reader their OWN chat and look like it worked.
  it('★ reports not-found when the id could not be loaded, instead of showing another thread', fakeAsync(async () => {
    await mountWith('not-mine');
    store.conversation.set(conversation('mine'));
    spyOn(store, 'openConversation').and.resolveTo();   // failure: the store keeps the old one

    fixture.detectChanges();
    tick();

    expect(fixture.componentInstance.notFound()).toBeTrue();
  }));

  it('does not re-fetch the conversation that is already live — expanding from the drawer', fakeAsync(async () => {
    await mountWith('c-42');
    store.conversation.set(conversation('c-42'));
    const open = spyOn(store, 'openConversation').and.resolveTo();

    fixture.detectChanges();
    tick();

    expect(open).not.toHaveBeenCalled();
    expect(fixture.componentInstance.notFound()).toBeFalse();
  }));

  it('shows the welcome, loading nothing, when the route carries no id', fakeAsync(async () => {
    await mountWith(null);
    const open = spyOn(store, 'openConversation').and.resolveTo();

    fixture.detectChanges();
    tick();

    expect(open).not.toHaveBeenCalled();
    expect(fixture.componentInstance.notFound()).toBeFalse();
  }));

  // Angular REUSES this component when only the route param changes, so a snapshot read would load
  // the first thread and never react again — the rail would highlight a new row over the old chat.
  it('★ follows a later change of the route param', fakeAsync(async () => {
    await mountWith('c-1');
    const open = spyOn(store, 'openConversation').and.callFake(async (id: string) => {
      store.conversation.set(conversation(id));
    });

    fixture.detectChanges();
    tick();

    params.next(convertToParamMap({ conversationId: 'c-2' }));
    tick();

    expect(open.calls.allArgs()).toEqual([['c-1'], ['c-2']]);
  }));

  it('puts a selected conversation in the URL, so a refresh comes back to it', fakeAsync(async () => {
    await mountWith(null);
    fixture.detectChanges();
    tick();

    void fixture.componentInstance.openConversation('c-9');
    tick();

    expect(router.navigate).toHaveBeenCalledWith(['/assistant', 'c-9']);
  }));

  it('moves the URL onto a newly started conversation', fakeAsync(async () => {
    await mountWith(null);
    fixture.detectChanges();
    tick();

    spyOn(store, 'startConversation').and.callFake(async () => {
      store.conversation.set(conversation('fresh'));
    });

    void fixture.componentInstance.startNew();
    tick();

    expect(router.navigate).toHaveBeenCalledWith(['/assistant', 'fresh']);
  }));
});

describe('AssistantPanelComponent — Expand carries the conversation', () => {
  let fixture: ComponentFixture<AssistantPanelComponent>;
  let store: AssistantStore;
  let router: jasmine.SpyObj<Router>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [AssistantPanelComponent, TranslateModule.forRoot()],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: AssistantApiService, useValue: {} },
        { provide: ActivatedRoute, useValue: { paramMap: of(convertToParamMap({})), queryParams: of({}) } },
        { provide: Router, useValue: jasmine.createSpyObj('Router', ['navigate', 'navigateByUrl']) },
      ],
    }).compileComponents();

    store = TestBed.inject(AssistantStore);
    router = TestBed.inject(Router) as jasmine.SpyObj<Router>;
    router.navigate.and.resolveTo(true);
    fixture = TestBed.createComponent(AssistantPanelComponent);
  });

  it('★ navigates to the live conversation, so the wide view shows the same thread', async () => {
    store.isOpen.set(true);
    store.conversation.set(conversation('live-1'));
    fixture.detectChanges();

    await fixture.componentInstance.expand();

    expect(router.navigate).toHaveBeenCalledWith(['/assistant', 'live-1']);
  });

  it('closes the drawer on the way out — it must not sit on top of the full page', async () => {
    store.isOpen.set(true);
    store.conversation.set(conversation('live-1'));
    fixture.detectChanges();

    await fixture.componentInstance.expand();

    expect(store.isOpen()).toBeFalse();
  });

  it('lands on the bare route when no conversation has been started yet', async () => {
    store.isOpen.set(true);
    store.conversation.set(null);
    fixture.detectChanges();

    await fixture.componentInstance.expand();

    expect(router.navigate).toHaveBeenCalledWith(['/assistant']);
  });
});

/**
 * Renaming: what saves, and what throws the edit away.
 *
 * ★ THE BUG THESE GUARD. The rename box was wired with `(blur)`, and `blur` does not bubble — ws-input
 * exposes no blur output either, so the handler bound on the host NEVER RAN. Clicking elsewhere closed
 * the menu and left the box open with no way out but Enter or Escape. The template now listens to
 * `focusout`, the bubbling twin, and abandons rather than saves.
 */
describe('Renaming a conversation — clicking away abandons, it does not save', () => {
  describe('from the chat header', () => {
    let fixture: ComponentFixture<AssistantPageComponent>;
    let store: AssistantStore;

    beforeEach(async () => {
      TestBed.configureTestingModule({
        imports: [AssistantPageComponent, TranslateModule.forRoot()],
        providers: [
          provideHttpClient(),
          provideHttpClientTesting(),
          { provide: AssistantApiService, useValue: {} },
          { provide: ActivatedRoute, useValue: { paramMap: of(convertToParamMap({})), queryParams: of({}) } },
          { provide: Router, useValue: jasmine.createSpyObj('Router', ['navigate', 'navigateByUrl', 'createUrlTree', 'serializeUrl']) },
        ],
      });
      TestBed.overrideComponent(AssistantPageComponent, { set: { template: '<div></div>', imports: [] } });
      await TestBed.compileComponents();

      store = TestBed.inject(AssistantStore);
      spyOn(store, 'loadEntitlement').and.resolveTo();
      spyOn(store, 'loadConversations').and.resolveTo();
      store.conversation.set(conversation('c-1'));
      fixture = TestBed.createComponent(AssistantPageComponent);
    });

    it('★ abandons the edit and stores nothing', async () => {
      const rename = spyOn(store, 'rename').and.resolveTo();

      fixture.componentInstance.startRename(new MouseEvent('click', { bubbles: true }));
      fixture.componentInstance.renameDraft.set('Half typed');
      fixture.componentInstance.cancelRename();

      expect(rename).not.toHaveBeenCalled();
      expect(fixture.componentInstance.renaming())
        .withContext('the box must close — leaving it open is the orphan bug').toBeFalse();
    });

    it('Enter still saves', async () => {
      const rename = spyOn(store, 'rename').and.resolveTo();

      fixture.componentInstance.startRename(new MouseEvent('click', { bubbles: true }));
      fixture.componentInstance.renameDraft.set('A real name');
      await fixture.componentInstance.commitRename();

      expect(rename).toHaveBeenCalledWith('c-1', 'A real name');
      expect(fixture.componentInstance.renaming()).toBeFalse();
    });

    // ★ Committing moves focus out, which fires focusout, which cancels. The commit closes the box
    // first precisely so that cancel lands on nothing — otherwise it would fight the save in flight.
    // ★ The bug, at the level it actually bit. Not "does cancelRename work" — it always did — but
    // whether a real click anywhere else on the page reaches it. Two fixes were needed: the box takes
    // focus on open (a box that never held focus can never lose it, so focusout never fired), and a
    // document listener that does not depend on focus at all.
    it('★ a click elsewhere on the page closes the box', () => {
      const rename = spyOn(store, 'rename').and.resolveTo();
      fixture.componentInstance.startRename(new MouseEvent('click', { bubbles: true }));
      fixture.detectChanges();

      const elsewhere = document.createElement('div');
      document.body.appendChild(elsewhere);
      elsewhere.dispatchEvent(new MouseEvent('click', { bubbles: true }));
      elsewhere.remove();

      expect(fixture.componentInstance.renaming())
        .withContext('the orphaned box is the bug').toBeFalse();
      expect(rename).not.toHaveBeenCalled();
    });

    // The classic self-closing bug for a document listener paired with open-on-click: the very event
    // that opens the box must not also reach the listener that closes it.
    it('★ the click that OPENS the box does not immediately close it', () => {
      fixture.componentInstance.startRename(new MouseEvent('click', { bubbles: true }));

      expect(fixture.componentInstance.renaming()).toBeTrue();
    });

    it('a click INSIDE the rename box leaves it open', () => {
      fixture.componentInstance.startRename(new MouseEvent('click', { bubbles: true }));
      fixture.detectChanges();

      const inside = document.createElement('div');
      inside.setAttribute('data-rename-box', '');
      document.body.appendChild(inside);
      inside.dispatchEvent(new MouseEvent('click', { bubbles: true }));
      inside.remove();

      expect(fixture.componentInstance.renaming()).toBeTrue();
    });

    it('★ a cancel arriving right after a commit does not undo it', async () => {
      const rename = spyOn(store, 'rename').and.resolveTo();

      fixture.componentInstance.startRename(new MouseEvent('click', { bubbles: true }));
      fixture.componentInstance.renameDraft.set('A real name');
      const saving = fixture.componentInstance.commitRename();
      fixture.componentInstance.cancelRename();     // the focusout the commit itself caused
      await saving;

      expect(rename).toHaveBeenCalledWith('c-1', 'A real name');
    });
  });
});

describe('AssistantConversationListComponent — rename and delete', () => {
  let fixture: ComponentFixture<AssistantConversationListComponent>;
  let store: AssistantStore;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [AssistantConversationListComponent, TranslateModule.forRoot()],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: AssistantApiService, useValue: {} },
        // The shared UI this list renders (confirmation modal, input) reaches for routing.
        { provide: ActivatedRoute, useValue: { paramMap: of(convertToParamMap({})), queryParams: of({}) } },
        { provide: Router, useValue: jasmine.createSpyObj('Router', ['navigate', 'navigateByUrl', 'createUrlTree', 'serializeUrl']) },
      ],
    }).compileComponents();

    store = TestBed.inject(AssistantStore);
    fixture = TestBed.createComponent(AssistantConversationListComponent);
  });

  it('emits the chosen id rather than opening it — the two hosts open it differently', () => {
    const seen: string[] = [];
    fixture.componentInstance.select.subscribe(id => seen.push(id));
    store.conversations.set([
      { id: 'c1', title: 'One', createdAt: '', updatedAt: '', messageCount: 1 },
    ]);
    fixture.detectChanges();

    // The row is a container and the TITLE is the button — a button inside a button is invalid HTML,
    // which is what pushed the rename control out of line in the first place.
    fixture.nativeElement.querySelector('.assistant-clist__open').click();

    expect(seen).toEqual(['c1']);
  });

  // ── The rail's grouping and search, at the component seam ────────────────
  //
  // The banding and matching rules themselves are covered in conversation-groups.spec; these pin the
  // WIRING: that the inputs reach them, and that a search flattens the bands.

  function withConversations(): void {
    const today = new Date();
    const older = new Date(today.getFullYear(), today.getMonth(), today.getDate() - 30);
    store.conversations.set([
      { id: 'a', title: 'Comisión de Anna', createdAt: '', updatedAt: today.toISOString(), messageCount: 1 },
      { id: 'b', title: 'Plan rules', createdAt: '', updatedAt: older.toISOString(), messageCount: 1 },
    ]);
  }

  it('shows one flat group when grouping is off — the drawer is untouched', () => {
    withConversations();
    fixture.detectChanges();

    expect(fixture.componentInstance.groups().length).toBe(1);
    expect(fixture.componentInstance.visibleCount()).toBe(2);
  });

  it('cuts the list into time bands when grouping is on', () => {
    withConversations();
    fixture.componentRef.setInput('grouped', true);
    fixture.detectChanges();

    expect(fixture.componentInstance.groups().map(g => g.key)).toEqual(['today', 'older']);
  });

  // ★ Bands answer "when was that conversation"; a search answers "where is the one about X". Keeping
  // the bands would scatter a handful of results across four headings and hide the answer.
  it('★ flattens the bands while the search box has text', () => {
    withConversations();
    fixture.componentRef.setInput('grouped', true);
    fixture.componentRef.setInput('query', 'plan');
    fixture.detectChanges();

    expect(fixture.componentInstance.groups().length).toBe(1);
    expect(fixture.componentInstance.groups()[0].items.map(i => i.id)).toEqual(['b']);
  });

  it('goes back to the bands when the search is cleared', () => {
    withConversations();
    fixture.componentRef.setInput('grouped', true);
    fixture.componentRef.setInput('query', 'plan');
    fixture.detectChanges();
    fixture.componentRef.setInput('query', '');
    fixture.detectChanges();

    expect(fixture.componentInstance.groups().map(g => g.key)).toEqual(['today', 'older']);
  });

  it('reports nothing visible when the search matches no thread', () => {
    withConversations();
    fixture.componentRef.setInput('query', 'zzz');
    fixture.detectChanges();

    expect(fixture.componentInstance.visibleCount()).toBe(0);
  });

  it('renames through the store', async () => {
    const rename = spyOn(store, 'rename').and.resolveTo();

    fixture.componentInstance.startRename('c1', 'Old name', new Event('click'));
    fixture.componentInstance.renameDraft.set('New name');
    await fixture.componentInstance.commitRename();

    expect(rename).toHaveBeenCalledWith('c1', 'New name');
    expect(fixture.componentInstance.renamingId()).toBeNull();
  });

  // An empty box is a user who changed their mind, not a request to erase the name.
  it('★ does not store an empty title', async () => {
    const rename = spyOn(store, 'rename').and.resolveTo();

    fixture.componentInstance.startRename('c1', 'Old name', new Event('click'));
    fixture.componentInstance.renameDraft.set('   ');
    await fixture.componentInstance.commitRename();

    expect(rename).not.toHaveBeenCalled();
  });

  // The label is a translated stand-in, not a name: pre-filling it would store the word "Untitled".
  it('★ starts an untitled thread with an empty box, not with the placeholder label', () => {
    fixture.componentInstance.startRename('c1', null, new Event('click'));

    expect(fixture.componentInstance.renameDraft()).toBe('');
  });

  // ★ Same fix, same reason, in the aside: focusout calls this, so clicking away must abandon.
  it('★ clicking away abandons the edit and stores nothing', () => {
    const rename = spyOn(store, 'rename').and.resolveTo();

    fixture.componentInstance.startRename('c1', 'Old name', new Event('click'));
    fixture.componentInstance.renameDraft.set('Half typed');
    fixture.componentInstance.cancelRename();

    expect(rename).not.toHaveBeenCalled();
    expect(fixture.componentInstance.renamingId())
      .withContext('the box must close — leaving it open is the orphan bug').toBeNull();
  });

  // ★ [autofocus] is load-bearing, not a nicety: focusout can only fire on a box that HELD focus, and
  // this box used to open unfocused — which is exactly why the first fix did not take.
  it('★ the rename box takes the caret when it opens', () => {
    store.conversations.set([
      { id: 'c1', title: 'Primera', createdAt: '', updatedAt: new Date().toISOString(), messageCount: 1 },
    ]);
    fixture.componentInstance.startRename('c1', 'Primera', new MouseEvent('click', { bubbles: true }));
    fixture.detectChanges();

    const input = fixture.nativeElement.querySelector('[data-testid="assistant-rename-input"] input');
    expect(document.activeElement).toBe(input);
  });

  it('★ a click elsewhere on the page closes the box', () => {
    const rename = spyOn(store, 'rename').and.resolveTo();
    store.conversations.set([
      { id: 'c1', title: 'Primera', createdAt: '', updatedAt: new Date().toISOString(), messageCount: 1 },
    ]);
    fixture.componentInstance.startRename('c1', 'Primera', new MouseEvent('click', { bubbles: true }));
    fixture.detectChanges();

    const elsewhere = document.createElement('div');
    document.body.appendChild(elsewhere);
    elsewhere.dispatchEvent(new MouseEvent('click', { bubbles: true }));
    elsewhere.remove();

    expect(fixture.componentInstance.renamingId())
      .withContext('the orphaned box is the bug').toBeNull();
    expect(rename).not.toHaveBeenCalled();
  });

  it('★ the click that OPENS the box does not immediately close it', () => {
    fixture.componentInstance.startRename('c1', 'Primera', new MouseEvent('click', { bubbles: true }));

    expect(fixture.componentInstance.renamingId()).toBe('c1');
  });

  it('escape abandons the rename without touching the store', () => {
    const rename = spyOn(store, 'rename').and.resolveTo();

    fixture.componentInstance.startRename('c1', 'Old name', new Event('click'));
    fixture.componentInstance.cancelRename();

    expect(rename).not.toHaveBeenCalled();
    expect(fixture.componentInstance.renamingId()).toBeNull();
  });

  it('deletes only after the confirmation is accepted', async () => {
    const remove = spyOn(store, 'remove').and.resolveTo();

    fixture.componentInstance.askDelete('c1', new Event('click'));
    expect(remove).not.toHaveBeenCalled();

    await fixture.componentInstance.confirmDelete();
    expect(remove).toHaveBeenCalledWith('c1');
  });

  it('cancelling the confirmation deletes nothing', () => {
    const remove = spyOn(store, 'remove').and.resolveTo();

    fixture.componentInstance.askDelete('c1', new Event('click'));
    fixture.componentInstance.cancelDelete();

    expect(remove).not.toHaveBeenCalled();
    expect(fixture.componentInstance.pendingDeleteId()).toBeNull();
  });
});

/**
 * The "jump to the newest message" button.
 *
 * ★ THE VISIBILITY RULE IS WHAT IS TESTED, not the floating. Headless lays out but the fixture's list
 * never grows tall enough to actually overflow, so the scroll container is driven directly: what has to
 * hold is that the button is offered only when there IS somewhere to jump to, and that it uses the same
 * near-bottom slack as the auto-scroll rather than a second, disagreeing threshold.
 */
describe('AssistantConversationComponent — jump to the newest message', () => {
  let fixture: ComponentFixture<AssistantConversationComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [AssistantConversationComponent, TranslateModule.forRoot()],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: AssistantApiService, useValue: {} },
        { provide: ActivatedRoute, useValue: { paramMap: of(convertToParamMap({})), queryParams: of({}) } },
        { provide: Router, useValue: jasmine.createSpyObj('Router', ['navigate', 'navigateByUrl']) },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(AssistantConversationComponent);
    fixture.detectChanges();
  });

  /** Puts the scroll container at a chosen distance from the bottom and reports it. */
  function scrollTo(distanceFromBottom: number): void {
    const el: HTMLElement = fixture.nativeElement
      .querySelector('[data-testid="assistant-messages"]');
    Object.defineProperty(el, 'scrollHeight', { value: 1000, configurable: true });
    Object.defineProperty(el, 'clientHeight', { value: 400, configurable: true });
    Object.defineProperty(el, 'scrollTop', { value: 600 - distanceFromBottom, configurable: true });
    el.dispatchEvent(new Event('scroll'));
    fixture.detectChanges();
  }

  function button(): HTMLElement | null {
    return fixture.nativeElement.querySelector('[data-testid="assistant-jump-latest"]');
  }

  // Nothing to jump to on a conversation that does not overflow — and no flash while it renders.
  it('★ is not offered before anything has scrolled', () => {
    expect(fixture.componentInstance.atBottom()).toBeTrue();
    expect(button()).toBeNull();
  });

  it('appears once there are messages below the fold', () => {
    scrollTo(400);

    expect(fixture.componentInstance.atBottom()).toBeFalse();
    expect(button()).toBeTruthy();
  });

  // ★ Not shown-then-disabled: at the bottom the control leaves rather than sitting there greyed out
  // promising a thing it cannot do — the same rule the Stop button follows.
  it('★ leaves again when the view returns to the newest message', () => {
    scrollTo(400);
    expect(button()).toBeTruthy();

    scrollTo(0);

    expect(fixture.componentInstance.atBottom()).toBeTrue();
    expect(button()).toBeNull();
  });

  // ★ The same slack the auto-scroll uses. A stricter threshold here would offer to take the user
  // somewhere they already are; a looser one would hide the button while content is still below.
  it('★ counts a hair off the bottom as being at the bottom', () => {
    scrollTo(10);

    expect(fixture.componentInstance.atBottom()).toBeTrue();
  });

  it('clicking it scrolls to the newest message', () => {
    scrollTo(400);
    const scroll = spyOn(fixture.componentInstance, 'scrollToBottom');

    button()!.click();

    expect(scroll).toHaveBeenCalledWith('smooth');
  });

  // A smooth scroll reports its position over several frames, so waiting for the scroll event would
  // leave the button up for the whole animation.
  it('★ hides immediately when the jump starts, not when the animation lands', () => {
    scrollTo(400);
    expect(fixture.componentInstance.atBottom()).toBeFalse();

    fixture.componentInstance.scrollToBottom('smooth');

    expect(fixture.componentInstance.atBottom()).toBeTrue();
  });
});

/**
 * ★ FOLLOWING THE ANSWER AS IT IS WRITTEN.
 *
 * The bug this covers was silent and total: the scroll effect watched `messages()`, and `messages()`
 * does not change during a stream — the row is appended only when the answer is FINISHED. So for the
 * whole reply the view sat still, the text grew below the fold, and the user had to chase it by hand.
 *
 * The rule has two halves and both matter: follow while the reader is at the bottom, and let go the
 * moment they scroll up to read something. Hijacking the view back down is worse than not following.
 */
describe('AssistantConversationComponent — the view follows the answer being written', () => {
  let fixture: ComponentFixture<AssistantConversationComponent>;
  let store: AssistantStore;
  let scrolled: ScrollBehavior[];

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [AssistantConversationComponent, TranslateModule.forRoot()],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: AssistantApiService, useValue: {} },
        { provide: ActivatedRoute, useValue: { paramMap: of(convertToParamMap({})), queryParams: of({}) } },
        { provide: Router, useValue: jasmine.createSpyObj('Router', ['navigate', 'navigateByUrl']) },
      ],
    }).compileComponents();

    store = TestBed.inject(AssistantStore);
    fixture = TestBed.createComponent(AssistantConversationComponent);
    scrolled = [];
    spyOn(fixture.componentInstance, 'scrollToBottom')
      .and.callFake((behavior: ScrollBehavior = 'auto') => { scrolled.push(behavior); });
    fixture.detectChanges();

    // ★ Drop the mount scroll before every test. Opening a conversation legitimately jumps to the
    // newest message, and counting that here would let a build that never follows a STREAM still
    // satisfy "it scrolled at least once" — the guards below would then be guarding nothing.
    TestBed.inject(ApplicationRef).tick();
    await fixture.whenStable();
    scrolled.length = 0;
  });

  /** Places the scroll container a chosen distance from the bottom. */
  function place(distanceFromBottom: number): void {
    const el: HTMLElement = fixture.nativeElement.querySelector('[data-testid="assistant-messages"]');
    Object.defineProperty(el, 'scrollHeight', { value: 1000, configurable: true });
    Object.defineProperty(el, 'clientHeight', { value: 400, configurable: true });
    Object.defineProperty(el, 'scrollTop', { value: 600 - distanceFromBottom, configurable: true });
  }

  /** One more fragment of the answer arriving. */
  async function streamFragment(text: string): Promise<void> {
    store.streamingReply.set(text);
    fixture.detectChanges();
    TestBed.inject(ApplicationRef).tick();
    await fixture.whenStable();
  }

  it('★ follows each fragment while the reader is at the bottom', async () => {
    place(0);
    await streamFragment('The balance');
    const afterFirst = scrolled.length;
    await streamFragment('The balance is');
    await streamFragment('The balance is 1.200 EUR');

    expect(afterFirst).toBeGreaterThan(0);
    expect(scrolled.length).toBeGreaterThan(afterFirst);
  });

  // ★ Instant, never smooth. A smooth scroll is an animation with a duration and the next token lands
  // before it ends; the animations queue and the text lurches behind the caret instead of following it.
  it('★ follows instantly rather than animating each fragment', async () => {
    place(0);
    await streamFragment('One');
    await streamFragment('One two');

    // Guarded: an empty list would satisfy `every` vacuously and this test would pass while the view
    // followed nothing at all.
    expect(scrolled.length).toBeGreaterThan(0);
    expect(scrolled.every(b => b === 'auto')).toBeTrue();
  });

  // ★ The other half of the rule. Someone who scrolled up is READING; dragging them back to the bottom
  // on the next token is the behaviour this whole design exists to avoid.
  it('★ lets go the moment the reader scrolls up, and does not drag them back', async () => {
    place(0);
    await streamFragment('One');
    const before = scrolled.length;
    // Guarded: without this the test would pass on a build that never follows at all, which is the
    // very bug the suite above exists to catch.
    expect(before).withContext('it must have been following first').toBeGreaterThan(0);

    place(400);                       // the reader scrolls up to re-read something
    await streamFragment('One two');
    await streamFragment('One two three');

    expect(scrolled.length).withContext('the view must not be hijacked').toBe(before);
  });

  it('★ re-engages once the reader is back at the bottom', async () => {
    place(0);
    await streamFragment('One');

    place(400);
    await streamFragment('One two');
    const whileReading = scrolled.length;

    place(0);                          // back at the newest message
    await streamFragment('One two three');

    expect(scrolled.length).toBeGreaterThan(whileReading);
  });

  // The same slack the button uses: a reader a hair off the bottom is still following.
  it('counts a hair off the bottom as still following', async () => {
    place(0);
    await streamFragment('One');
    const before = scrolled.length;

    place(10);
    await streamFragment('One two');

    expect(scrolled.length).toBeGreaterThan(before);
  });
});

/**
 * The composer's shape, measured for real.
 *
 * ★ THESE RUN IN CHROME, WHICH DOES LAYOUT — and that is the only reason they are worth writing. The
 * decision comes from a hidden mirror element that is actually laid out and measured; in an environment
 * that reports zero for every box (jsdom) the component deliberately answers "stacked" and a test there
 * would be asserting the fail-safe, not the rule. So these use REAL text at the REAL width and let the
 * browser wrap it, rather than stubbing heights — stubbed heights are what let four broken versions of
 * this ship with a green suite.
 */
describe('AssistantConversationComponent — the composer changes shape', () => {
  let fixture: ComponentFixture<AssistantConversationComponent>;

  /** Long enough to wrap at any composer width this component is ever given. */
  const LONG_TEXT = 'Lorem ipsum dolor sit amet consectetur adipiscing elit sed do eiusmod tempor '
    + 'incididunt ut labore et dolore magna aliqua ut enim ad minim veniam quis nostrud '
    + 'exercitation ullamco laboris nisi ut aliquip ex ea commodo consequat duis aute irure.';

  function layout(): string {
    return fixture.componentInstance.composerLayout();
  }

  /** The card carries the shape as a data attribute — one element, read by descendant selectors. */
  function stackedRow(): HTMLElement | null {
    return fixture.nativeElement.querySelector('.assistant-composer[data-stacked="true"]');
  }

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [AssistantConversationComponent, TranslateModule.forRoot()],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: AssistantApiService, useValue: {} },
        { provide: ActivatedRoute, useValue: { paramMap: of(convertToParamMap({})), queryParams: of({}) } },
        { provide: Router, useValue: jasmine.createSpyObj('Router', ['navigate', 'navigateByUrl']) },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(AssistantConversationComponent);
    // The fixture needs a real width for the mirror to have a width to measure against.
    (fixture.nativeElement as HTMLElement).style.width = '420px';
    document.body.appendChild(fixture.nativeElement);
    fixture.detectChanges();
    TestBed.inject(ApplicationRef).tick();
    await fixture.whenStable();
  });

  afterEach(() => (fixture.nativeElement as HTMLElement).remove());

  it('★ stacks once the text needs a second line', () => {
    fixture.componentInstance.onDraftChange(LONG_TEXT);
    fixture.detectChanges();

    expect(layout()).toBe('stacked');
  });

  // ★ The seam the pure rule cannot see: the class has to reach the element the stylesheet selects.
  it('★ and the card actually carries the stacked attribute', () => {
    fixture.componentInstance.onDraftChange(LONG_TEXT);
    fixture.detectChanges();

    expect(stackedRow()).not.toBeNull();
  });

  it('is a pill with a short line', () => {
    fixture.componentInstance.onDraftChange('hola');
    fixture.detectChanges();

    expect(layout()).toBe('pill');
    expect(stackedRow()).toBeNull();
  });

  it('returns to a pill when the text is cleared', () => {
    fixture.componentInstance.onDraftChange(LONG_TEXT);
    fixture.detectChanges();
    expect(layout()).toBe('stacked');

    fixture.componentInstance.onDraftChange('');
    fixture.detectChanges();

    expect(layout()).toBe('pill');
  });

  // ★ THE FAIL-SAFE. Before the view exists there is nothing to measure, and the answer must be the
  // shape that cannot break text. A default of `pill` turns every failed measurement into the bug this
  // composer shipped four times.
  it('★ answers stacked when there is nothing to measure yet', () => {
    const fresh = TestBed.createComponent(AssistantConversationComponent);

    expect(fresh.componentInstance.composerLayout()).toBe('stacked');
  });
});
