import { computed, inject, Injectable, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { AssistantApiService } from '../services/assistant.api.service';
import { AuthService } from '../../../core/services/auth.service';
import {
  AssistantConversation,
  AssistantConversationSummary,
  AssistantMessage,
  AssistantProgressStep,
  isCancelledReply,
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

  /**
   * True when the assistant is withheld ONLY because the workspace is on Free. Drives the locked
   * entry point; stays false for every other refusal, which keeps rendering nothing at all.
   */
  readonly requiresUpgrade = signal(false);

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
      this.requiresUpgrade.set(result.requiresUpgrade === true);
    } catch {
      // A failed check means "no button", never "assume yes". The backend gates every call anyway,
      // so guessing generously here would only produce a button that 403s. The upsell is suppressed
      // too: we do not know the plan, and inventing an upgrade prompt is worse than showing nothing.
      this.entitled.set(false);
      this.requiresUpgrade.set(false);
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
    // ★ ONE RETRY, TWO WAYS IN. The button under a failed turn and the one beside a stopped answer run
    // the SAME thing: re-answer the last stored question, without writing it again. They are kept as
    // separate signals only because they are separate SIGHTS — the failure card must not appear over a
    // turn the user stopped on purpose — and the two can never both be true, since a cancelled reply is
    // a stored answer and `retryable` needs the thread to be waiting on one.
    const pending = this.retryable() ?? this.retryableCancelled();
    if (!pending || this.sending()) {
      return;
    }

    await this.exchange(pending.content, pending.wasPersisted);
  }

  /**
   * The handle on the request in flight — what Stop actually pulls.
   *
   * ★ ABORTING THE FETCH IS THE WHOLE MECHANISM, on both sides. The browser stops reading, and the
   * server sees the connection go: its request token cancels, the call to the model is dropped mid
   * answer (no more tokens are paid for words nobody will read), and it writes what had arrived as a
   * cancelled turn. One signal, no second endpoint to keep in step with this one.
   */
  private controller: AbortController | null = null;

  /**
   * True from the instant Stop is pressed until the failed exchange has finished unwinding.
   *
   * ★ IT IS WHAT TELLS A CANCELLATION APART FROM A FAILURE, and they arrive identically: aborting the
   * fetch throws into the same catch a dead connection would. Without this the user would press Stop
   * and be told the assistant could not answer — blamed for a fault, and offered a retry for a turn
   * they deliberately ended.
   */
  private cancelling = false;

  /**
   * Stops the answer being written, and keeps what was written.
   *
   * ★ THE INPUT IS FREE IMMEDIATELY, which is the point of the button: someone presses Stop because
   * they want to ask something ELSE, so `sending` falls as the request unwinds and the composer is
   * usable before the stored row has even been read back.
   *
   * ★ THE CANCELLED TURN IS ON SCREEN BEFORE THE SERVER IS ASKED ANYTHING, and that is a correction of
   * how this worked at first. The original version froze the partial text and waited for the stored row
   * to read back before showing anything final — and the read LOST THE RACE: the server writes that row
   * while the aborted request unwinds, which is after the browser has already fired its question. Both
   * asks came back with the turn not yet stored, the frozen text was cleared as "it never existed", and
   * the user watched their answer VANISH on the click that was supposed to preserve it. Only a refresh
   * brought it back.
   *
   * So the order is inverted: the turn is written into the thread here, immediately, from the words that
   * were on screen — and the server's own row replaces it quietly when it lands. What is shown is never
   * invented; it is exactly the text the user just read, which is also exactly what the backend is in
   * the middle of storing.
   */
  async cancel(): Promise<void> {
    const controller = this.controller;

    if (!this.sending() || controller === null) {
      return;
    }

    this.cancelling = true;
    this.progressSteps.set([]);

    const conversationId = this.conversation()?.id ?? null;
    // Read BEFORE the abort: the exchange's own unwinding must not be racing us for this value.
    const partial = this.streamingReply();

    controller.abort();

    this.streamingReply.set(null);

    // ★ SET HERE, SYNCHRONOUSLY, NOT LEFT TO THE EXCHANGE'S `finally`. That runs a few microtasks later,
    // once the aborted stream has finished throwing — so leaving it to that made "the composer is free"
    // depend on scheduling, and made the reconcile below unable to tell "the turn I just cancelled is
    // still unwinding" from "a new question is being answered". Pressing Stop IS the end of this turn;
    // saying so immediately is both truer and simpler.
    this.sending.set(false);

    // ★ NOTHING WRITTEN YET MEANS NOTHING TO KEEP. Stopping during the classifier or a lookup leaves
    // the server with an empty answer, and it stores nothing rather than a blank bubble — so there is
    // no row to wait for and no partial to show. The question simply stands unanswered, which is what
    // it is, and the composer is already free for the next one.
    if (partial === null || partial.length === 0) {
      return;
    }

    this.appendCancelledTurn(partial.trim());

    if (conversationId !== null) {
      await this.reconcileCancelled(conversationId);
    }
  }

  /**
   * The id carried by the cancelled turn until the server's own row replaces it.
   *
   * A CONSTANT, not a generated id: there can only ever be one of these at a time — it is the answer
   * that was being written, and there is only one of those — and a stable value makes it obvious in a
   * debugger that this row is the local stand-in rather than something the backend sent.
   */
  static readonly PENDING_CANCELLED_ID = 'pending-cancelled';

  /**
   * Puts the stopped answer into the thread, as the turn it is.
   *
   * ★ IT GOES INTO `messages`, NOT BESIDE THEM. A separate "here is the cancelled bubble" signal would
   * render after every stored message, so the moment the user asked their next question — which is the
   * whole reason they pressed Stop — the stopped answer would jump BELOW it and the thread would read
   * out of order. Put in sequence, it is simply where it happened.
   */
  private appendCancelledTurn(content: string): void {
    const current = this.conversation();

    if (!current || content.length === 0) {
      return;
    }

    const lastSequence = current.messages.reduce((max, m) => Math.max(max, m.sequence), -1);

    this.conversation.set({
      ...current,
      messages: [
        ...current.messages,
        {
          id: AssistantStore.PENDING_CANCELLED_ID,
          role: 'Assistant',
          content,
          payload: null,
          sequence: lastSequence + 1,
          createdAt: new Date().toISOString(),
          status: 'Cancelled',
        },
      ],
      // The thread ends on an answer now, so it is not waiting on one. Without this the failure card
      // would appear beside a turn the user ended on purpose.
      lastTurnUnanswered: false,
    });
  }

  /**
   * How long to wait before each further ask. See {@link reconcileCancelled}.
   *
   * Backing off rather than hammering: the first gap covers the ordinary case (the write lands while
   * the request unwinds), and the later ones cover a server busy enough that it has not noticed the
   * dropped connection yet.
   */
  private static readonly RECONCILE_DELAYS_MS = [0, 300, 900, 2000];

  /**
   * Swaps the local stand-in for the row the server actually stored.
   *
   * ★ IT IS A CORRECTION, NEVER A REMOVAL. The stand-in is already correct — same words, same state —
   * so this exists only to replace it with the authoritative row (real id, server's truncation, server's
   * timestamp). If every ask comes back without it, the stand-in simply STAYS: taking it away would
   * reproduce the exact bug this ordering was changed to fix, and the next time the conversation is
   * opened the server's version is what renders anyway.
   */
  private async reconcileCancelled(conversationId: string): Promise<void> {
    for (const delay of AssistantStore.RECONCILE_DELAYS_MS) {
      if (delay > 0) {
        await new Promise((resolve) => setTimeout(resolve, delay));
      }

      let refreshed: AssistantConversation;
      try {
        refreshed = await firstValueFrom(this.api.getConversation(conversationId));
      } catch {
        return;
      }

      // The panel may have moved on while we were asking — a different thread, or this one already
      // busy answering something new. A snapshot taken before that started would erase it.
      if (this.conversation()?.id !== conversationId || this.sending()) {
        return;
      }

      const last = [...refreshed.messages].sort((a, b) => a.sequence - b.sequence).at(-1);

      if (last && isCancelledReply(last)) {
        this.conversation.set(refreshed);
        await this.loadConversations();
        return;
      }
    }
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
   * The words of a turn that never reached the server, or null when there are none.
   *
   * ★ EXPOSED SO THE COMPOSER CAN HAND THEM BACK, and deliberately NOT so the thread can show them.
   * The user's turn belongs to the server — see the `user` frame — precisely so that a reload rebuilds
   * the conversation with no help from this browser. Putting an unsent message into `messages` would
   * create a second version of it that a refresh cannot find, which is the failure this design avoids.
   * The composer is the honest place for it: the message is not IN the conversation, it is still
   * something the user is about to say.
   */
  readonly unsentText = computed<string | null>(() => this.unsentFailure()?.content ?? null);

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

    // ★ THE EXACT RECORD BEATS THE INFERENCE, AND THAT ORDER IS THE SAFETY NET. `unsentFailure` holds
    // the very words that failed to send; the branch below INFERS a question from the stored thread.
    // Read the other way round — as it was — a lying `lastTurnUnanswered` shadowed the exact record
    // and the retry re-answered a different, older question while the user's own message was lost.
    // The flag is guarded now, but a local fact should never lose to a derivation regardless: if the
    // two ever disagree again, the one that cannot be wrong is this one.
    const unsent = this.unsentFailure();
    if (unsent) {
      return { content: unsent.content, wasPersisted: false };
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

    return null;
  });

  /**
   * What the "Try again" beside a STOPPED answer would re-run, or null when there is no such offer.
   *
   * ★ ONLY WHEN THE STOPPED ANSWER IS THE LAST THING IN THE THREAD. Retrying re-answers the last stored
   * question, so on an older cancelled turn — one the user has since asked past — the button would say
   * "try again" and re-answer something else entirely. A control whose label describes a different
   * message than the one it sits under is worse than no control.
   *
   * ★ ALWAYS `wasPersisted: true`. There IS a stored assistant row here, which means the question that
   * produced it certainly reached the server. This can never be the never-arrived case.
   */
  readonly retryableCancelled = computed<{ content: string; wasPersisted: boolean } | null>(() => {
    // Nothing to offer while one is in flight — same rule as `retryable`, and for the same reason.
    if (this.sending()) {
      return null;
    }

    const ordered = [...(this.conversation()?.messages ?? [])].sort((a, b) => a.sequence - b.sequence);
    const last = ordered.at(-1);

    if (!last || !isCancelledReply(last)) {
      return null;
    }

    const question = [...ordered].reverse().find((m) => m.role === 'User');

    return question ? { content: question.content, wasPersisted: true } : null;
  });

  private async exchange(trimmed: string, isRetry: boolean): Promise<void> {
    this.sending.set(true);
    this.error.set(null);
    this.errorKey.set(null);
    this.unsentFailure.set(null);
    this.cancelling = false;
    // One controller per turn: an AbortController is single-use, so reusing last turn's would arrive
    // already fired and stop this request before it began.
    const controller = new AbortController();
    this.controller = controller;
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
        conversationId, trimmed, token, controller.signal, isRetry)) {
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
            //
            // ★ ONLY IF THE QUESTION WAS ACTUALLY STORED. Setting this unconditionally made the flag
            // LIE: with nothing committed, the thread does not end on an unanswered question at all —
            // it ends wherever it ended before this attempt. And a lying flag was not a cosmetic
            // problem, because `retryable` trusts it: it went looking for the last stored User turn,
            // found the PREVIOUS, already-answered question, and offered to retry that. The user
            // watched their own message vanish and a second answer appear under the old one.
            this.markThreadUnanswered(persisted);
            this.unsentFailure.set(persisted ? null : { content: trimmed });
            break;
        }
      }

      await this.loadConversations();
    } catch {
      // ★ A CANCELLATION LANDS HERE TOO — aborting the fetch throws exactly like a dead connection —
      // and it must take none of this. `cancel()` owns that path: it keeps the partial answer on
      // screen until the stored row replaces it, leaves the thread answered, and offers no retry,
      // because the user did not suffer a failure, they made a decision.
      if (!this.cancelling) {
        this.streamingReply.set(null);
        this.progressSteps.set([]);
        this.errorKey.set('ASSISTANT.ERROR_UNAVAILABLE');
        // Same guard as the error frame above, and it matters MORE here: a transport failure is the
        // likeliest way for a turn to die before the server ever saw it.
        this.markThreadUnanswered(persisted);
        this.unsentFailure.set(persisted ? null : { content: trimmed });
      }
    } finally {
      // ★ ONLY IF THIS IS STILL THE TURN IN FLIGHT. A cancelled exchange frees the composer at the
      // click and then keeps unwinding for a few microtasks; the user can start a new question inside
      // that window, and this clean-up — belonging to a request that is already over — would otherwise
      // clear the NEW turn's sending flag and drop its abort handle, leaving a Stop button with nothing
      // to stop.
      if (this.controller === controller) {
        this.sending.set(false);
        this.controller = null;
      }
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
  /**
   * Records that the thread now ends on a question nobody answered.
   *
   * ★ THE PARAMETER IS THE POINT. It takes `persisted` rather than being called only when true,
   * because the caller reads better for it — and because a future error path that forgets the question
   * is a call that has to say, out loud, which of the two situations it is in. `false` is not "do
   * nothing by accident"; it is "the server holds no question, so the flag would be false anyway".
   */
  private markThreadUnanswered(persisted: boolean): void {
    const current = this.conversation();
    if (current && persisted) {
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
