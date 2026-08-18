import {
  Component,
  DestroyRef,
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
import { TranslateModule, TranslateService } from '@ngx-translate/core';
import { AssistantStore } from '../state/assistant.store';
import { Router } from '@angular/router';
import {
  AssistantMessage,
  internalRouteOf,
  isCancelledReply,
  isPlaceholderReply,
  isUntitled,
  phaseLabelKey,
} from '../models/assistant.model';
import { WsButtonComponent } from '../../../shared/ui/ws-button/ws-button.component';
import { WsTextareaComponent } from '../../../shared/ui/ws-textarea/ws-textarea.component';
import { WsConfirmationModalComponent } from '../../../shared/ui';
import { IconComponent } from '../../../shared/components/icon/icon.component';
import { AssistantMarkdownPipe } from '../pipes/assistant-markdown.pipe';
import { AssistantMathDirective } from '../pipes/assistant-math.directive';
import { STARTER_PROMPTS, StarterPrompt, placeholderRange } from './starter-prompts';

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
    WsConfirmationModalComponent,
    IconComponent,
    AssistantMarkdownPipe,
    AssistantMathDirective,
  ],
  templateUrl: './assistant-panel.component.html',
  styleUrl: './assistant-panel.component.scss',
})
export class AssistantPanelComponent {
  readonly store = inject(AssistantStore);
  private readonly injector = inject(Injector);
  private readonly translate = inject(TranslateService);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);

  readonly draft = signal('');
  readonly pendingDeleteId = signal<string | null>(null);

  /**
   * True once the answer has been slow enough to be worth explaining.
   *
   * ★ THE SECOND LINE SAYS WHY, NOT WHERE. It never claims a stage — "consulting the documentation",
   * "looking up the transaction" — because the front end does not know which of those is happening,
   * and a progress message timed to a stopwatch is theatre. It would also be the one place in this
   * assistant that states something it cannot verify, in a feature whose entire design is about not
   * doing that. What IS true at every moment: work is in progress and it may take a few seconds.
   */
  readonly waitingLong = signal(false);

  /**
   * How many reported steps make a list worth showing instead of the plain loader.
   *
   * Three, because two is the floor: every turn understands the question and then writes an answer, so
   * a two-item list is what "nothing special happened" looks like — and a checklist that appears on
   * every request, ticks twice and disappears is movement without information. At three the list is
   * telling the user something they could not have assumed: the guide was consulted, or their records
   * were read.
   */
  private static readonly MIN_STEPS_TO_LIST = 3;

  /** True when this turn reported enough distinct work to be worth listing. */
  readonly showSteps = computed(
    () => this.store.progressSteps().length >= AssistantPanelComponent.MIN_STEPS_TO_LIST);

  /** The translation key for a reported phase — see `phaseLabelKey` for the unknown-phase rule. */
  phaseLabel(phase: string): string {
    return phaseLabelKey(phase);
  }

  /**
   * How long the answer may take before the wait is explained.
   *
   * Long enough that an ordinary documentation answer never trips it — those come back well inside
   * this — and short enough to arrive before a tool-calling turn starts feeling broken. A message that
   * appears on every request would be noise, and noise is what people learn to stop reading.
   */
  private static readonly LONG_WAIT_MS = 4500;

  private longWaitTimer: ReturnType<typeof setTimeout> | null = null;

  readonly canSend = computed(() => this.draft().trim().length > 0 && !this.store.sending());

  private readonly messagesEl = viewChild<ElementRef<HTMLElement>>('messages');

  /** The composer, so a starter prompt can put its text in it and select the placeholder. */
  private readonly composer = viewChild(WsTextareaComponent);

  /**
   * The example questions under the welcome. Shown only while the conversation is empty — the
   * same condition the welcome itself uses, so they leave together the moment there is anything
   * to read, and come back on a new conversation because that is an empty one again.
   */
  readonly starters: readonly StarterPrompt[] = STARTER_PROMPTS;

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

    // Reopening while the exit is still playing cancels it: the element is being shown again, so it
    // must not also be told to leave. Without this the panel would come back wearing the class that
    // fades it out.
    effect(() => {
      if (this.store.isOpen()) {
        this.closing.set(false);
      }
    });

    // ── The long wait ────────────────────────────────────────────────────────
    // `streamingReply` is the whole signal this needs: '' means the request is out and nothing has
    // come back, anything longer means the answer has started arriving. So the clock starts on the
    // empty string and stops the moment there is a first token — no extra state to keep in step.
    effect(() => {
      const waiting = this.store.streamingReply() === '';

      untracked(() => {
        if (!waiting) {
          this.stopLongWaitClock();
          return;
        }

        if (this.longWaitTimer === null) {
          this.longWaitTimer = setTimeout(
            () => this.waitingLong.set(true), AssistantPanelComponent.LONG_WAIT_MS);
        }
      });
    });

    this.destroyRef.onDestroy(() => this.stopLongWaitClock());
  }

  /** Stops the clock and takes the explanation back down. Safe to call when it was never started. */
  private stopLongWaitClock(): void {
    if (this.longWaitTimer !== null) {
      clearTimeout(this.longWaitTimer);
      this.longWaitTimer = null;
    }

    this.waitingLong.set(false);
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

  /**
   * True when the row is an answer the user stopped, so the template says where it stops.
   *
   * Read from the STORED row — see `isCancelledReply`. Nothing about the click is remembered here.
   */
  isCancelled(message: AssistantMessage): boolean {
    return isCancelledReply(message);
  }

  /**
   * ★ THE LINK INTERCEPTOR — what makes the assistant's guidance usable rather than destructive.
   *
   * The assistant now answers "how do I create a plan?" with steps and a link to `/plans/new`. Rendered
   * Markdown gives that a plain `<a href>`, and a plain `<a href>` is a FULL PAGE LOAD: Angular is torn
   * down and rebuilt, and the conversation the user was reading — the thing that just told them where
   * to go — is gone at the exact moment they acted on it. The most valuable click in the feature would
   * be the one that destroys it.
   *
   * So internal links are routed instead: `preventDefault` stops the browser, and Angular's Router
   * moves the app underneath the panel, which stays open with the instructions still on screen.
   *
   * ★ ONE HANDLER ON THE CONTAINER, not one per link. The anchors come from `[innerHTML]`, so there is
   * no template to bind on — delegation is the only way to reach them, and it keeps working for every
   * link of every message including the one still streaming.
   *
   * ★ EXTERNAL LINKS ARE NOT TOUCHED. `internalRouteOf` returns null for them and this returns early,
   * so they open in a new tab with `noopener noreferrer` exactly as the Markdown pipe set them up. The
   * router is for this app; anything else is the browser's job.
   */
  onMarkdownClick(event: MouseEvent): void {
    const anchor = (event.target as HTMLElement | null)?.closest?.('a');
    if (!anchor) {
      return;
    }

    // The literal attribute, not `anchor.href` — the DOM property resolves "/plans/new" to a full
    // absolute URL, which would fail the leading-slash test and let every internal link through.
    const route = internalRouteOf(anchor.getAttribute('href'));
    if (route === null) {
      return;
    }

    // A modified click is the user deliberately asking the BROWSER for a new tab or window. Hijacking
    // it into an in-app navigation would override an intent they expressed on purpose.
    if (event.ctrlKey || event.metaKey || event.shiftKey || event.altKey || event.button !== 0) {
      return;
    }

    event.preventDefault();
    void this.router.navigateByUrl(route);
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

  /**
   * A starter prompt was clicked: put its sentence in the composer, ready to be completed.
   *
   * ★ IT FILLS, IT DOES NOT SEND — and that is the whole design of the feature. Every one of these
   * sentences has a hole in it ("[payee name]"), so sending on click would ask the assistant for a
   * payee literally called "[payee name]", find nobody, and teach the user in one click that the thing
   * does not work.
   *
   * ★ THE PLACEHOLDER IS LEFT SELECTED so the next keystroke replaces it. Dropping the caret at the
   * end instead would leave the user to find the brackets, delete them, and type inside — three
   * fiddly steps in the exact moment the feature exists to make easy.
   *
   * The draft signal is set as well as the box, rather than relying on the textarea to announce it:
   * `canSend()` and `send()` read the signal, and a composer whose visible text cannot be sent would
   * be a worse bug than the one being fixed.
   */
  useStarter(starter: StarterPrompt): void {
    const text = this.translate.instant(starter.promptKey);

    this.draft.set(text);

    const { start, end } = placeholderRange(text);
    this.composer()?.fill(text, start, end);
  }

  /**
   * Stops the answer being written. See the store — the words already on screen are KEPT, and the
   * composer is usable again immediately, which is the whole reason someone presses this.
   */
  async cancel(): Promise<void> {
    await this.store.cancel();
  }

  /** Re-answers the last failed question. See the store — it does NOT re-send the message. */
  async retry(): Promise<void> {
    await this.store.retry();
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
