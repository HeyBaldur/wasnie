import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { TranslateModule } from '@ngx-translate/core';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { of } from 'rxjs';
import { AssistantConversationComponent } from './assistant-conversation.component';
import { AssistantStore } from '../state/assistant.store';
import { AssistantApiService } from '../services/assistant.api.service';
import { AssistantConversation } from '../models/assistant.model';

/**
 * The copy menu closes when you choose from it.
 *
 * ★★ THE POPOVER CLOSES ON AN OUTSIDE CLICK, AND A MENU ITEM IS INSIDE IT. So picking "Copy answer"
 * copied the text and then left the menu sitting open on top of the answer it had just copied — the
 * user had to click somewhere else to dismiss a menu they were already done with.
 *
 * ★ AND THIS IS ASSERTED THROUGH THE DOM ON PURPOSE. The fix is one call added to a template binding,
 * which is exactly the shape of change that compiles, renders and silently does nothing — a dead
 * `(clicked)` on the conversation list shipped that way earlier in this project. A test on the
 * component's own state would pass with the binding removed.
 */
describe('AssistantConversationComponent — the copy menu closes on choosing', () => {
  let fixture: ComponentFixture<AssistantConversationComponent>;
  let store: AssistantStore;

  const CONVERSATION: AssistantConversation = {
    id: 'c1',
    title: 'A thread',
    createdAt: '',
    updatedAt: '',
    messages: [
      { id: 'm1', role: 'User', content: 'what is my balance', payload: null, sequence: 0, createdAt: '' },
      { id: 'm2', role: 'Assistant', content: 'You earned **1,200 EUR**.', payload: null, sequence: 1, createdAt: '' },
    ],
    lastTurnUnanswered: false,
  };

  beforeEach(async () => {
    // The same surface the sibling specs stub: only what mounting the conversation touches.
    const api = jasmine.createSpyObj<AssistantApiService>('AssistantApiService', [
      'getEntitlement', 'listConversations', 'getConversation', 'startConversation',
      'postMessage', 'streamMessage', 'renameConversation', 'deleteConversation',
    ]);
    api.getEntitlement.and.returnValue(of({ enabled: true, requiresUpgrade: false }));
    api.listConversations.and.returnValue(of({ items: [], nextCursor: null, pinned: [] }));

    await TestBed.configureTestingModule({
      imports: [AssistantConversationComponent, TranslateModule.forRoot()],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: AssistantApiService, useValue: api },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(AssistantConversationComponent);
    store = TestBed.inject(AssistantStore);

    store.isOpen.set(true);
    store.conversation.set(CONVERSATION);
    fixture.detectChanges();
  });

  /** The panel only exists in the DOM while the popover is open — see ws-popover's template. */
  function menuIsOpen(): boolean {
    return !!fixture.nativeElement.querySelector('[data-testid="assistant-copy-answer"]');
  }

  function openMenu(): void {
    const trigger: HTMLElement =
      fixture.nativeElement.querySelector('[data-testid="assistant-copy-menu"]');
    expect(trigger).withContext('the copy menu trigger is on the answer').toBeTruthy();
    trigger.click();
    fixture.detectChanges();
  }

  it('opens on the trigger', () => {
    expect(menuIsOpen()).toBeFalse();
    openMenu();
    expect(menuIsOpen()).toBeTrue();
  });

  it('★★ closes after "copy answer"', () => {
    openMenu();

    const item: HTMLElement =
      fixture.nativeElement.querySelector('[data-testid="assistant-copy-answer"]');
    item.click();
    fixture.detectChanges();

    expect(menuIsOpen())
      .withContext('the menu must not stay open over the answer it just copied')
      .toBeFalse();
  });

  it('★★ closes after "copy markdown"', () => {
    openMenu();

    const item: HTMLElement =
      fixture.nativeElement.querySelector('[data-testid="assistant-copy-markdown"]');
    item.click();
    fixture.detectChanges();

    expect(menuIsOpen()).toBeFalse();
  });
});
