import { computed, inject, Injectable, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { AssistantApiService } from '../services/assistant.api.service';
import { AuthService } from '../../../core/services/auth.service';
import {
  AssistantConversation,
  AssistantConversationSummary,
  AssistantMessage,
} from '../models/assistant.model';

/**
 * The assistant panel's state: open/closed, the current thread, and the history list.
 *
 * Root-provided because the panel lives in the app shell and its trigger lives in the topbar — two
 * components that never meet in the tree. The store is what connects them.
 */
@Injectable({ providedIn: 'root' })
export class AssistantStore {
  private readonly api = inject(AssistantApiService);
  private readonly auth = inject(AuthService);

  // ── Entitlement ───────────────────────────────────────────────────────────
  // Null until asked. The trigger renders nothing while unknown, so a slow answer never flashes a
  // button the user is not entitled to.
  readonly entitled = signal<boolean | null>(null);

  // ── Panel ─────────────────────────────────────────────────────────────────
  readonly isOpen = signal(false);
  readonly historyOpen = signal(false);

  // ── Data ──────────────────────────────────────────────────────────────────
  readonly conversation = signal<AssistantConversation | null>(null);
  readonly conversations = signal<AssistantConversationSummary[]>([]);
  readonly loading = signal(false);
  readonly sending = signal(false);
  readonly error = signal<string | null>(null);

  /**
   * The answer as it arrives, before it is a stored row. Null when nothing is streaming — including
   * after a failure, because the server persisted nothing and half an answer on screen would be a
   * message the user cannot find again.
   */
  readonly streamingReply = signal<string | null>(null);

  /** A translation key for the last failure, rendered by the panel in the reader's language. */
  readonly errorKey = signal<string | null>(null);

  readonly messages = computed<AssistantMessage[]>(() => this.conversation()?.messages ?? []);
  readonly hasConversation = computed(() => this.conversation() !== null);

  async loadEntitlement(): Promise<void> {
    try {
      const result = await firstValueFrom(this.api.getEntitlement());
      this.entitled.set(result.enabled);
    } catch {
      // A failed check means "no button", never "assume yes". The backend gates every call anyway,
      // so guessing generously here would only produce a button that 403s.
      this.entitled.set(false);
    }
  }

  /** Opens the panel. Loads the history once so the user can reach previous threads immediately. */
  async open(): Promise<void> {
    this.isOpen.set(true);
    if (this.conversations().length === 0) {
      await this.loadConversations();
    }
  }

  close(): void {
    this.isOpen.set(false);
    this.historyOpen.set(false);
  }

  toggleHistory(): void {
    this.historyOpen.update((v) => !v);
  }

  async loadConversations(): Promise<void> {
    this.loading.set(true);
    this.error.set(null);
    try {
      this.conversations.set(await firstValueFrom(this.api.listConversations()));
    } catch {
      this.error.set('LOAD_FAILED');
    } finally {
      this.loading.set(false);
    }
  }

  async startConversation(): Promise<void> {
    this.loading.set(true);
    this.error.set(null);
    try {
      const created = await firstValueFrom(this.api.startConversation());
      this.conversation.set(created);
      this.historyOpen.set(false);
      await this.loadConversations();
    } catch {
      this.error.set('START_FAILED');
    } finally {
      this.loading.set(false);
    }
  }

  async openConversation(conversationId: string): Promise<void> {
    this.loading.set(true);
    this.error.set(null);
    try {
      this.conversation.set(await firstValueFrom(this.api.getConversation(conversationId)));
      this.historyOpen.set(false);
    } catch {
      this.error.set('LOAD_FAILED');
    } finally {
      this.loading.set(false);
    }
  }

  /**
   * Sends a turn and renders the answer as it is written.
   *
   * Starts a thread first if there is none, so the user can just type — the empty panel should not make
   * them press "new conversation" before they are allowed to talk.
   *
   * ★ THE STREAMED TEXT IS NOT THE STORED MESSAGE. `streamingReply` holds what has arrived so far and
   * is rendered as a temporary bubble; when the `done` frame carries the persisted row, that row
   * replaces it. If the stream fails instead, the partial text is DISCARDED — the server stored nothing
   * for the assistant, and leaving half an answer on screen would show the user something that does not
   * exist and will not be there when they come back.
   */
  async send(content: string): Promise<void> {
    const trimmed = content.trim();
    if (trimmed.length === 0 || this.sending()) {
      return;
    }

    this.sending.set(true);
    this.error.set(null);
    this.errorKey.set(null);
    this.streamingReply.set(null);

    try {
      if (!this.conversation()) {
        const created = await firstValueFrom(this.api.startConversation());
        this.conversation.set(created);
      }

      const conversationId = this.conversation()!.id;
      const token = this.auth.getAccessToken();

      for await (const frame of this.api.streamMessage(conversationId, trimmed, token)) {
        switch (frame.type) {
          case 'user':
            // The SERVER's row for what we typed — never an optimistic local copy, which would be a
            // second version of the message that can drift from the stored one.
            this.appendMessage(frame.message!);
            this.streamingReply.set('');
            break;

          case 'delta':
            this.streamingReply.update((soFar) => (soFar ?? '') + (frame.delta ?? ''));
            break;

          case 'done':
            this.streamingReply.set(null);
            this.appendMessage(frame.message!);
            break;

          case 'error':
            this.streamingReply.set(null);
            this.errorKey.set(frame.errorKey ?? 'ASSISTANT.ERROR_UNAVAILABLE');
            break;
        }
      }

      await this.loadConversations();
    } catch {
      this.streamingReply.set(null);
      this.errorKey.set('ASSISTANT.ERROR_UNAVAILABLE');
    } finally {
      this.sending.set(false);
    }
  }

  /** Appends one persisted row to the open conversation. */
  private appendMessage(message: AssistantMessage): void {
    const current = this.conversation();
    if (!current) {
      return;
    }
    this.conversation.set({ ...current, messages: [...current.messages, message] });
  }

  async rename(conversationId: string, title: string): Promise<void> {
    const trimmed = title.trim();
    if (trimmed.length === 0) {
      return;
    }

    try {
      await firstValueFrom(this.api.renameConversation(conversationId, trimmed));
      const current = this.conversation();
      if (current?.id === conversationId) {
        this.conversation.set({ ...current, title: trimmed });
      }
      await this.loadConversations();
    } catch {
      this.error.set('RENAME_FAILED');
    }
  }

  async remove(conversationId: string): Promise<void> {
    try {
      await firstValueFrom(this.api.deleteConversation(conversationId));
      if (this.conversation()?.id === conversationId) {
        this.conversation.set(null);
      }
      await this.loadConversations();
    } catch {
      this.error.set('DELETE_FAILED');
    }
  }
}
