import {
  Component,
  ElementRef,
  Injector,
  afterNextRender,
  computed,
  effect,
  inject,
  signal,
  untracked,
  viewChild,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { TranslateModule } from '@ngx-translate/core';
import { AssistantStore } from '../state/assistant.store';
import { AssistantMessage, isPlaceholderReply, isUntitled } from '../models/assistant.model';
import { WsButtonComponent } from '../../../shared/ui/ws-button/ws-button.component';
import { WsTextareaComponent } from '../../../shared/ui/ws-textarea/ws-textarea.component';
import { WsEmptyStateComponent } from '../../../shared/ui/ws-empty-state/ws-empty-state.component';
import { WsConfirmationModalComponent } from '../../../shared/ui';
import { IconComponent } from '../../../shared/components/icon/icon.component';
import { AssistantMarkdownPipe } from '../pipes/assistant-markdown.pipe';

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
    CommonModule,
    FormsModule,
    TranslateModule,
    WsButtonComponent,
    WsTextareaComponent,
    WsEmptyStateComponent,
    WsConfirmationModalComponent,
    IconComponent,
    AssistantMarkdownPipe,
  ],
  templateUrl: './assistant-panel.component.html',
  styleUrl: './assistant-panel.component.scss',
})
export class AssistantPanelComponent {
  readonly store = inject(AssistantStore);
  private readonly injector = inject(Injector);

  readonly draft = signal('');
  readonly pendingDeleteId = signal<string | null>(null);

  readonly canSend = computed(() => this.draft().trim().length > 0 && !this.store.sending());

  private readonly messagesEl = viewChild<ElementRef<HTMLElement>>('messages');

  /**
   * How close to the bottom still counts as "following the conversation", in px. Generous enough to
   * survive a half-scrolled line or a rounding difference between browsers — a user one pixel off the
   * bottom is still reading the newest message, not the archive.
   */
  private static readonly NEAR_BOTTOM_PX = 80;

  /**
   * What was on screen last time we looked: the panel's open state plus which conversation. A change
   * in either means a FRESH view, which must appear already at the bottom rather than scroll there.
   */
  private lastView: string | null = null;

  constructor() {
    // ★ THE TIMING. This effect runs BEFORE the template has rendered the new turns, which is exactly
    // why it does not scroll here: the container has not grown yet, so scrollHeight is the old one and
    // the jump would land short. It decides WHETHER and HOW to scroll — reading the pre-render scroll
    // position, which is the only moment "was the user at the bottom?" can still be answered — and
    // hands the actual move to afterNextRender, which fires once the DOM is painted.
    effect(() => {
      const conversationId = this.store.conversation()?.id ?? null;
      const isOpen = this.store.isOpen();
      // Read so the effect re-runs when a turn is appended.
      this.store.messages().length;

      const view = `${isOpen}:${conversationId}`;
      // A reopened panel counts as fresh too: closing destroys the container, and the new one starts
      // at scrollTop 0 with no signal having changed.
      const isFreshView = view !== this.lastView;
      // ★ untracked: isNearBottom() reads the viewChild signal, and depending on it would re-run this
      // effect the moment the container resolves — a second pass that no longer looks "fresh" and so
      // would animate the very jump that must be instant. The user would watch the view travel down
      // from the oldest message, which is the bug this whole change exists to remove.
      const wasFollowing = untracked(() => this.isNearBottom());
      this.lastView = view;

      if (!isOpen) {
        return;
      }

      // ★ Do not yank someone out of the history they are reading. A new turn only pulls the view
      // down if they were already at the bottom; a fresh view always starts at the bottom.
      if (!isFreshView && !wasFollowing) {
        return;
      }

      // Opening shows the newest message ALREADY in place — no visible travel from the top. A turn
      // arriving in a conversation you are watching may glide, because you are there to see it.
      const behavior: ScrollBehavior = isFreshView ? 'auto' : 'smooth';

      afterNextRender(() => this.scrollToBottom(behavior), { injector: this.injector });
    });
  }

  /** True when the view is at (or within a hair of) the newest message — or when there is nothing yet. */
  private isNearBottom(): boolean {
    const el = this.messagesEl()?.nativeElement;
    if (!el) {
      return true;
    }

    const distance = el.scrollHeight - el.scrollTop - el.clientHeight;
    return distance <= AssistantPanelComponent.NEAR_BOTTOM_PX;
  }

  /** Public so a test can observe the wiring; headless lays out, but not to a scrollable height here. */
  scrollToBottom(behavior: ScrollBehavior = 'auto'): void {
    const el = this.messagesEl()?.nativeElement;
    if (!el) {
      return;
    }

    el.scrollTo({ top: el.scrollHeight, behavior });
  }

  /** True while the thread has no name yet, so the template renders the translated label instead. */
  isUntitled(title: string | null | undefined): boolean {
    return isUntitled(title);
  }

  /** True when the row is the stand-in reply, so the template renders the translated copy instead. */
  isPlaceholder(message: AssistantMessage): boolean {
    return isPlaceholderReply(message);
  }

  async send(): Promise<void> {
    if (!this.canSend()) {
      return;
    }
    const text = this.draft();
    this.draft.set('');
    await this.store.send(text);
  }

  onDraftChange(value: string): void {
    this.draft.set(value);
  }

  async startNew(): Promise<void> {
    await this.store.startConversation();
  }

  async openConversation(id: string): Promise<void> {
    await this.store.openConversation(id);
  }

  askDelete(id: string, event: Event): void {
    // Stops the row's own click from also opening the conversation being deleted.
    event.stopPropagation();
    this.pendingDeleteId.set(id);
  }

  async confirmDelete(): Promise<void> {
    const id = this.pendingDeleteId();
    if (id) {
      await this.store.remove(id);
    }
    this.pendingDeleteId.set(null);
  }

  cancelDelete(): void {
    this.pendingDeleteId.set(null);
  }

  close(): void {
    this.store.close();
  }
}
