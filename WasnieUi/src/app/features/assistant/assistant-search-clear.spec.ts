import { ComponentFixture, TestBed, fakeAsync, tick } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TranslateModule } from '@ngx-translate/core';
import { provideRouter } from '@angular/router';
import { of, throwError } from 'rxjs';
import { AssistantStore } from './state/assistant.store';
import { AssistantApiService } from './services/assistant.api.service';
import { AssistantConversationListComponent } from './conversation-list/assistant-conversation-list.component';
import { AssistantConversationPage, AssistantConversationSummary } from './models/assistant.model';

/**
 * Clearing the search box brings every conversation back.
 *
 * ★ THROUGH THE DOM, NOT THE STORE. The store-level test for this already passed while the bug was on
 * screen, which is exactly what makes a component test worth writing: what broke was the plumbing
 * between the box and the store, and a test that calls `setSearch` directly steps over the very wire
 * that was cut.
 */

function summary(id: string): AssistantConversationSummary {
  return { id, title: id, createdAt: '', updatedAt: '2026-08-26T09:00:00Z', messageCount: 1 };
}

function page(ids: string[], nextCursor: string | null = null): AssistantConversationPage {
  return { items: ids.map(summary), nextCursor, pinned: [] };
}

describe('AssistantConversationListComponent — clearing the search', () => {
  let fixture: ComponentFixture<AssistantConversationListComponent>;
  let store: AssistantStore;
  let api: jasmine.SpyObj<AssistantApiService>;

  const ALL = ['alpha', 'beta', 'gamma'];
  const MATCH = ['beta'];

  beforeEach(async () => {
    api = jasmine.createSpyObj<AssistantApiService>('AssistantApiService', [
      'getEntitlement', 'listConversations', 'getConversation', 'startConversation',
      'postMessage', 'streamMessage', 'renameConversation', 'deleteConversation',
    ]);
    api.getEntitlement.and.returnValue(of({ enabled: true, requiresUpgrade: false }));
    api.listConversations.and.callFake((_cursor?: string | null, search?: string | null) =>
      of(search ? page(MATCH) : page(ALL)));

    await TestBed.configureTestingModule({
      imports: [AssistantConversationListComponent, TranslateModule.forRoot()],
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: AssistantApiService, useValue: api },
      ],
    }).compileComponents();

    store = TestBed.inject(AssistantStore);
    fixture = TestBed.createComponent(AssistantConversationListComponent);
  });

  function box(): HTMLInputElement {
    return fixture.nativeElement.querySelector('[data-testid="assistant-search"] input');
  }

  function type(text: string): void {
    const el = box();
    el.value = text;
    el.dispatchEvent(new Event('input'));
    fixture.detectChanges();
  }

  function rows(): string[] {
    return Array.from(
      fixture.nativeElement.querySelectorAll('[data-testid="assistant-history-item"]'),
    ).map((r) => (r as HTMLElement).textContent!.trim());
  }

  // ══ ★★ The buttons are actually wired ═════════════════════════════════

  it('★★ clicking Load more actually loads more', async () => {
    // ★★ REPORTED FROM RUNTIME: the button appeared and clicking it did nothing at all. The
    // template bound `(clicked)`, and WsButtonComponent has NO outputs — it renders a native <button>
    // and lets the DOM click bubble. Angular reads `(clicked)` on a component as a listener for a
    // CUSTOM EVENT of that name, so it compiled, rendered, and silently never fired. No error, no
    // warning, no console line.
    //
    // ★ AND THIS TEST GOES THROUGH THE DOM FOR EXACTLY THAT REASON. Calling `loadMore()` on the
    // component passes while the button is dead — the store-level tests for paging did pass the whole
    // time. What broke was the wire between the click and the handler, and only a real click crosses it.
    api.listConversations.and.returnValue(of(page(['alpha'], 'CURSOR-1')));
    await store.loadConversations();
    fixture.detectChanges();

    api.listConversations.calls.reset();
    api.listConversations.and.returnValue(of(page(['beta'], null)));

    const button: HTMLElement =
      fixture.nativeElement.querySelector('[data-testid="assistant-load-more"] button')
      ?? fixture.nativeElement.querySelector('[data-testid="assistant-load-more"]');
    expect(button).withContext('the button is on screen').toBeTruthy();

    button.click();
    await fixture.whenStable();
    fixture.detectChanges();

    expect(api.listConversations).withContext('the click reached the store').toHaveBeenCalled();
    expect(api.listConversations.calls.mostRecent().args[0]).toBe('CURSOR-1');
    expect(rows()).toEqual(['alpha', 'beta']);
  });

  it('★ clicking Retry after a failed batch actually retries', async () => {
    // Same wiring bug, same button primitive, one line away in the same template.
    api.listConversations.and.returnValue(throwError(() => new Error('down')));
    await store.loadConversations();
    fixture.detectChanges();

    const retry: HTMLElement =
      fixture.nativeElement.querySelector('[data-testid="assistant-list-retry"] button')
      ?? fixture.nativeElement.querySelector('[data-testid="assistant-list-retry"]');
    expect(retry).withContext('the failure offers a retry').toBeTruthy();

    api.listConversations.calls.reset();
    api.listConversations.and.returnValue(of(page(ALL)));

    retry.click();
    await fixture.whenStable();
    fixture.detectChanges();

    expect(api.listConversations).toHaveBeenCalled();
    expect(rows()).toEqual(ALL);
  });

  it('★ typing a search filters, and clearing the box brings everything back', fakeAsync(() => {
    void store.loadConversations();
    tick();
    fixture.detectChanges();
    expect(rows()).toEqual(ALL);

    type('beta');
    tick(300);
    fixture.detectChanges();
    expect(rows()).toEqual(MATCH);

    // ★ THE REPORTED BUG: emptying the box has to restore the list.
    type('');
    tick(300);
    fixture.detectChanges();

    expect(store.searchTerm()).toBe('');
    expect(rows()).toEqual(ALL, 'clearing the search must bring every conversation back');
  }));

  it('★ deleting one character at a time restores the list too', fakeAsync(() => {
    void store.loadConversations();
    tick();

    type('beta');
    tick(300);
    expect(store.searchTerm()).toBe('beta');

    for (const text of ['bet', 'be', 'b', '']) {
      type(text);
      tick(300);
    }
    fixture.detectChanges();

    expect(store.searchTerm()).toBe('');
    expect(rows()).toEqual(ALL);
  }));

  it('★ a SECOND live copy of the list follows the search too', fakeAsync(() => {
    // The drawer lives in the app shell and the page owns a rail, so two copies of this component can
    // be on screen at once. They share one store, so they have to agree about what is being searched —
    // a stale box beside a filtered list is the same lie as an empty one.
    //
    // ★ ASSERTED ON THE COMPONENT'S OWN SIGNAL, NOT ON THE <input> VALUE. The DOM path is already
    // pinned by the two tests above, on the fixture the event actually goes through; here the box is
    // three change-detection layers away from the store (effect → signal → ngModel → element) and
    // asserting the element would be testing when the harness flushes rather than whether the sync works.
    void store.loadConversations();
    tick();

    const second = TestBed.createComponent(AssistantConversationListComponent);
    second.detectChanges();

    type('beta');
    tick(300);
    second.detectChanges();

    expect(second.componentInstance.searchBox()).toBe('beta');

    type('');
    tick(300);
    second.detectChanges();

    expect(second.componentInstance.searchBox()).toBe('', 'and it follows the clearing as well');
    second.destroy();
  }));

  it('★★ a REMOUNTED list does not show an empty box over a filtered list', fakeAsync(() => {
    // ★★ THE DRAWER DESTROYS THIS COMPONENT EVERY TIME THE HISTORY PANEL IS TOGGLED, and the page
    // mounts a second copy of it. The box's text is the COMPONENT's; the applied term is the STORE's.
    // So a component that comes back fresh renders an EMPTY box over a list that is still filtered —
    // and the user, looking at an empty search box, quite reasonably reports that clearing the search
    // did not bring their conversations back.
    void store.loadConversations();
    tick();
    type('beta');
    tick(300);
    expect(store.searchTerm()).toBe('beta');

    // The drawer is closed and reopened: same store, brand new component.
    fixture.destroy();
    fixture = TestBed.createComponent(AssistantConversationListComponent);
    fixture.detectChanges();
    tick(300);
    fixture.detectChanges();

    expect(box().value)
      .withContext('the box must not lie about what is being searched')
      .toBe(store.searchTerm());
  }));
});
