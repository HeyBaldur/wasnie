import { Component, effect, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { TranslateModule } from '@ngx-translate/core';
import { AssistantStore } from '../state/assistant.store';
import { isUntitled } from '../models/assistant.model';
import { WsButtonComponent } from '../../../shared/ui/ws-button/ws-button.component';
import { IconComponent } from '../../../shared/components/icon/icon.component';
import { AssistantConversationComponent } from '../conversation/assistant-conversation.component';
import { AssistantConversationListComponent } from '../conversation-list/assistant-conversation-list.component';

/**
 * The assistant's slide-over panel: right side, opens and closes over the app.
 *
 * ★ WHY THIS IS A FEATURE COMPONENT AND NOT A `WsDrawer` PRIMITIVE. There is no slide-over in the
 * design system, and adding one is a separate decision (DESIGN_SYSTEM §10.3) rather than something to
 * improvise mid-feature. The documented precedent is the import wizard's progress bar: built as local
 * CSS in the one component that needs it, elevated to `shared/ui/` only once a SECOND feature wants it.
 * The assistant is the first. If a second slide-over appears, this is what gets promoted — the tokens
 * and the elevation are already the house ones, so promotion is a move, not a rewrite.
 *
 * ★ NO MODEL IS CALLED anywhere in this feature. The assistant's reply is a sentinel the backend
 * stored, translated here at render time (see `placeholderFor`).
 */
@Component({
  selector: 'app-assistant-panel',
  standalone: true,
  imports: [
    TranslateModule,
    WsButtonComponent,
    IconComponent,
    AssistantConversationComponent,
    AssistantConversationListComponent,
  ],
  templateUrl: './assistant-panel.component.html',
  styleUrl: './assistant-panel.component.scss',
})
export class AssistantPanelComponent {
  readonly store = inject(AssistantStore);
  private readonly router = inject(Router);

  constructor() {
    // Reopening while the exit is still playing cancels it: the element is being shown again, so it
    // must not also be told to leave. Without this the panel would come back wearing the class that
    // fades it out.
    effect(() => {
      if (this.store.isOpen()) {
        this.closing.set(false);
      }
    });
  }

  /** True while the thread has no name yet, so the template renders the translated label instead. */
  isUntitled(title: string | null | undefined): boolean {
    return isUntitled(title);
  }

  async startNew(): Promise<void> {
    await this.store.startConversation();
  }

  async openConversation(id: string): Promise<void> {
    await this.store.openConversation(id);
  }

  /**
   * Shows the conversation the user is reading on the full page, and closes the drawer behind it.
   *
   * ★ NOTHING IS CARRIED ACROSS. The conversation lives in `AssistantStore`, which both views read, so
   * this navigates and that is all — the messages are already there when the page mounts. The id goes
   * in the URL for the OTHER reason: so a refresh, a bookmark or a pasted link comes back to this same
   * thread instead of an empty one.
   *
   * With no conversation started yet there is no id to put in the URL, so it lands on the bare route
   * and the page shows the welcome — the same thing the drawer was showing.
   */
  async expand(): Promise<void> {
    const id = this.store.conversation()?.id;
    this.close();
    await this.router.navigate(id ? ['/assistant', id] : ['/assistant']);
  }

  /**
   * True while the panel is playing its exit — the ONLY thing keeping it mounted at that point.
   *
   * ★ IT DOES NOT MEAN "STILL OPEN". The first version kept `store.isOpen` true until the animation
   * finished, and that was the bug: for those 200ms the application still believed the assistant was
   * open, so anything that re-rendered or re-created the panel in that window brought it back at full
   * opacity — which is what "the chat opens whenever I click a sidebar item" actually was. The panel
   * had never closed; it was sitting there invisible, waiting to be shown again.
   *
   * So the shared state closes IMMEDIATELY and this flag only holds the element in the DOM long enough
   * to animate. A stuck flag can now leave at worst an invisible, click-through element — never a
   * panel that reopens itself.
   */
  readonly closing = signal(false);

  /** The name of the exit keyframes, so a child's animation cannot be mistaken for the panel's. */
  private static readonly EXIT_ANIMATION = 'assistant-panel-out';

  /**
   * Closes the panel at once, and keeps the element around just long enough to animate it away.
   *
   * ★ THE STATE CLOSES FIRST, THE PICTURE CATCHES UP. See `closing` for why the other order was wrong.
   *
   * ★ REDUCED MOTION SKIPS THE HOLD ENTIRELY. With `animation: none` the `animationend` event never
   * fires, so an element held for it would linger forever — for exactly the people who asked for less
   * movement. There is nothing to wait for, so nothing waits.
   */
  close(): void {
    if (!this.store.isOpen()) {
      return;
    }

    this.store.close();

    if (!AssistantPanelComponent.prefersReducedMotion()) {
      this.closing.set(true);
    }
  }

  /** The exit finished: the element can go. */
  onCloseAnimationEnd(event: AnimationEvent): void {
    // The panel contains other animations — the typing dots run forever — so the panel's OWN exit is
    // identified by name rather than by "an animation ended somewhere in here".
    if (event.animationName !== AssistantPanelComponent.EXIT_ANIMATION) {
      return;
    }

    this.closing.set(false);
  }

  private static prefersReducedMotion(): boolean {
    return typeof window !== 'undefined'
      && typeof window.matchMedia === 'function'
      && window.matchMedia('(prefers-reduced-motion: reduce)').matches;
  }
}
