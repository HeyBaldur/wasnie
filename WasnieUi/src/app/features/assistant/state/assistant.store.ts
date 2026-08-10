import { computed, inject, Injectable, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { AssistantApiService } from '../services/assistant.api.service';
import { AuthService } from '../../../core/services/auth.service';
import {
  AssistantConversation,
  AssistantConversationSummary,
  AssistantMessage,
  AssistantProgressStep,
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

  /**
   * The steps of the turn in flight, in the order the server announced them.
   *
   * ★ APPEND-ONLY, AND ONLY FROM THE SERVER. Nothing here is predicted: a step exists because the
   * backend said it started, and turns green because the backend said it finished. A stream that sends
   * no progress frames at all — an older backend, or a turn that fails before any work — simply leaves
   * this empty, and the panel falls back to the plain loader it has always shown.
   */
  readonly progressSteps = signal<AssistantProgressStep[]>([]);

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

    await this.exchange(trimmed, false);
  }

  /**
   * Ask the server to answer the last question AGAIN, after a failure.
   *
   * ★ IT DOES NOT RE-SEND THE MESSAGE. The backend commits the user's turn before it calls the model —
   * which is exactly what makes a failure survivable — so the question is already in the thread. Sending
   * it again would put the same words in the conversation twice, and the user would watch their own
   * message duplicate as the reward for pressing Retry. The retry flag tells the server to re-answer
   * what it already stored.
   *
   * The content is passed for the case where the turn NEVER reached the server (a transport failure
   * before the `user` frame); there is nothing stored to re-answer then, so it is a normal send.
   */
  async retry(): Promise<void> {
    const pending = this.retryable();
    if (!pending || this.sending()) {
      return;
    }

    await this.exchange(pending.content, pending.wasPersisted);
  }

  /**
   * A failure THIS session saw, for the one case the server cannot report: the turn never reached it.
   *
   * When the request died before the `user` frame — no network, the tab lost connectivity — nothing was
   * stored, so there is no trailing question for the server to derive a failure from, and this is the
   * only record that one happened. It is session-only by nature: a refresh genuinely loses that turn,
   * because it never existed anywhere but here.
   */
  private readonly unsentFailure = signal<{ content: string } | null>(null);

  /**
   * What a Retry would re-run, or null when there is nothing to retry — which is what hides the alert.
   *
   * ★ THE SERVER'S ANSWER WINS, AND THAT IS WHAT SURVIVES THE REFRESH. `lastTurnUnanswered` is derived
   * from the stored turns on every read, so a reloaded page reconstructs the failure with no help from
   * session memory. The local signal only covers the turn that never reached the server at all.
   */
  readonly retryable = computed<{ content: string; wasPersisted: boolean } | null>(() => {
    // ★ NOTHING TO OFFER WHILE ONE IS IN FLIGHT. A retry of a reloaded failure begins with the thread
    // still marked unanswered — it only stops being so when the answer lands — so without this the
    // card would sit next to the very loader that is resolving it, inviting a second retry of the
    // request already running.
    if (this.sending()) {
      return null;
    }

    const conversation = this.conversation();

    if (conversation?.lastTurnUnanswered) {
      const lastQuestion = [...conversation.messages]
        .sort((a, b) => a.sequence - b.sequence)
        .reverse()
        .find((m) => m.role === 'User');

      if (lastQuestion) {
        return { content: lastQuestion.content, wasPersisted: true };
      }
    }

    const unsent = this.unsentFailure();
    return unsent ? { content: unsent.content, wasPersisted: false } : null;
  });

  private async exchange(trimmed: string, isRetry: boolean): Promise<void> {
    this.sending.set(true);
    this.error.set(null);
    this.errorKey.set(null);
    this.unsentFailure.set(null);
    // Last turn's steps belong to last turn. Cleared here rather than at the end of the previous
    // exchange so a finished list is never left half-shown while the next request is being opened.
    this.progressSteps.set([]);

    // ★ THE TYPING DOTS ARE NORMALLY LIT BY THE `user` FRAME — and a retry never sends one, because
    // the question is already stored and echoing it back would duplicate it on screen. So the retry
    // has to light them itself, or the user presses the button and watches nothing happen until the
    // first fragment lands: a button that looks broken at the exact moment it is working.
    //
    // Empty string, not null: the panel reads null as "nothing is streaming" and an empty reply as
    // "something is coming", which is precisely the state a retry starts in.
    this.streamingReply.set(isRetry ? '' : null);

    // Tracks whether the server got as far as storing the question. It decides whether a retry
    // re-answers the stored turn or sends a fresh one — get this wrong and the thread duplicates.
    let persisted = isRetry;

    try {
      if (!this.conversation()) {
        const created = await firstValueFrom(this.api.startConversation());
        this.conversation.set(created);
      }

      const conversationId = this.conversation()!.id;
      const token = this.auth.getAccessToken();

      for await (const frame of this.api.streamMessage(
        conversationId, trimmed, token, undefined, isRetry)) {
        switch (frame.type) {
          case 'user':
            persisted = true;
            // The SERVER's row for what we typed — never an optimistic local copy, which would be a
            // second version of the message that can drift from the stored one.
            //
            // ★ THE FLAG IS NOT TOUCHED HERE. The thread does technically end on an unanswered
            // question at this instant, but it is unanswered because the answer is ON ITS WAY — and
            // saying so put the failure card on screen beside the typing dots, telling the user their
            // question had failed while it was being answered. "Waiting" is not "failed"; only the
            // error frame below knows the difference.
            this.appendMessage(frame.message!);
            this.streamingReply.set('');
            break;

          case 'progress':
            this.recordStep(frame.phase, frame.state);
            break;

          case 'delta':
            this.streamingReply.update((soFar) => (soFar ?? '') + (frame.delta ?? ''));
            break;

          case 'done':
            this.streamingReply.set(null);
            this.progressSteps.set([]);
            // ★ The answer landed, so the thread no longer ends on a question — the derived failure
            // clears itself. Marking the conversation answered here keeps the open screen honest
            // without a second round trip; a reload would compute the same thing.
            this.appendMessage(frame.message!, { threadUnanswered: false });
            this.unsentFailure.set(null);
            break;

          case 'error':
            this.streamingReply.set(null);
            // The steps go with the loader. Leaving a half-ticked checklist above a failure card would
            // show the user how far it got as if that were an outcome — nothing was persisted, and the
            // only thing to do with the turn is retry it.
            this.progressSteps.set([]);
            this.errorKey.set(frame.errorKey ?? 'ASSISTANT.ERROR_UNAVAILABLE');
            // NOW the thread is genuinely waiting on nothing — which is what the server will report
            // on the next read too. A stored question needs no local record beyond this flag; only
            // the turn that never arrived does.
            this.markThreadUnanswered();
            this.unsentFailure.set(persisted ? null : { content: trimmed });
            break;
        }
      }

      await this.loadConversations();
    } catch {
      this.streamingReply.set(null);
      this.progressSteps.set([]);
      this.errorKey.set('ASSISTANT.ERROR_UNAVAILABLE');
      this.markThreadUnanswered();
      this.unsentFailure.set(persisted ? null : { content: trimmed });
    } finally {
      this.sending.set(false);
    }
  }

  /**
   * Folds one `progress` frame into the visible list of steps.
   *
   * ★ A `done` FOR A STEP NOBODY ANNOUNCED IS STILL SHOWN, as an already-finished one. Frames can only
   * arrive in order over one stream, so this cannot really happen today — but the alternative, dropping
   * it, would silently lose a step the server did perform, and losing information is the one failure
   * mode this list must not have.
   */
  private recordStep(phase: string | undefined, state: string | undefined): void {
    if (!phase) {
      return;
    }

    const done = state === 'done';

    this.progressSteps.update((steps) => {
      const index = steps.findIndex((s) => s.phase === phase);

      if (index === -1) {
        return [...steps, { phase, done }];
      }

      // A repeated `start` must not un-tick a step that already finished.
      if (!done) {
        return steps;
      }

      const next = [...steps];
      next[index] = { phase, done: true };
      return next;
    });
  }

  /** Records that the thread is waiting on an answer that will not come. */
  private markThreadUnanswered(): void {
    const current = this.conversation();
    if (current) {
      this.conversation.set({ ...current, lastTurnUnanswered: true });
    }
  }

  /**
   * Appends one persisted row to the open conversation.
   *
   * `threadUnanswered` mirrors the rule the SERVER applies — a thread ending on a user turn is one
   * waiting for an answer — so the open screen and a reload of it cannot disagree. It is passed
   * explicitly rather than inferred from the row's role because the caller knows which frame it is
   * handling, and a rule guessed in two places is a rule that eventually differs in two places.
   */
  private appendMessage(
    message: AssistantMessage, options?: { threadUnanswered: boolean }): void {
    const current = this.conversation();
    if (!current) {
      return;
    }
    this.conversation.set({
      ...current,
      messages: [...current.messages, message],
      lastTurnUnanswered: options?.threadUnanswered ?? current.lastTurnUnanswered,
    });
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
