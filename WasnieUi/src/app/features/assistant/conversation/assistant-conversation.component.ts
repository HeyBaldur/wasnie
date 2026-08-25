import {
  Component,
  DestroyRef,
  ElementRef,
  Injector,
  afterNextRender,
  computed,
  effect,
  inject,
  input,
  signal,
  untracked,
  viewChild,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { TranslateModule, TranslateService } from '@ngx-translate/core';

import { AssistantStore } from '../state/assistant.store';
import {
  AssistantMessage,
  internalRouteOf,
  isCancelledReply,
  isPlaceholderReply,
  phaseLabelKey,
} from '../models/assistant.model';
import { WsButtonComponent } from '../../../shared/ui/ws-button/ws-button.component';
import { WsTextareaComponent } from '../../../shared/ui/ws-textarea/ws-textarea.component';
import { IconComponent } from '../../../shared/components/icon/icon.component';
import { AssistantMarkdownPipe } from '../pipes/assistant-markdown.pipe';
import { AssistantMathDirective } from '../pipes/assistant-math.directive';
import { STARTER_PROMPTS, StarterPrompt, placeholderRange } from './../panel/starter-prompts';
import { ComposerLayout, composerLayoutFor, composerMaxHeight } from './composer-layout';
import { ComposerMirror } from './composer-mirror';

/**
 * The conversation itself, with no opinion about where it is shown.
 *
 * ★ ONE COPY, TWO HOMES. This markup and this logic used to live inside the slide-over panel. The
 * full-page assistant at /assistant needs exactly the same message list, streaming bubble, step list,
 * failed/cancelled turn handling, welcome, starter prompts and composer — so it was extracted rather
 * than copied. A second copy of this template is the thing this component exists to prevent: it is the
 * hardest markup in the feature (Markdown + KaTeX + sanitiser + link interception), and two versions
 * of it would drift the first time either was touched.
 *
 * ★ IT OWNS NO CONVERSATION STATE. Everything read here comes from `AssistantStore`
 * (`providedIn: 'root'`), which is what lets the drawer and the page show the SAME live conversation:
 * they are two views of one store, not two chats. What IS local is presentational only — the draft
 * text, the scroll position and the long-wait clock.
 *
 * ★ NO MODEL IS CALLED anywhere in this feature. The assistant's stand-in reply is a sentinel the
 * backend stored, translated at render time.
 */
@Component({
  selector: 'app-assistant-conversation',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    TranslateModule,
    WsButtonComponent,
    WsTextareaComponent,
    IconComponent,
    AssistantMarkdownPipe,
    AssistantMathDirective,
  ],
  templateUrl: './assistant-conversation.component.html',
  styleUrl: './assistant-conversation.component.scss',
  host: {
    '[class.assistant-conversation--wide]': 'wide()',
  },
})
export class AssistantConversationComponent {
  readonly store = inject(AssistantStore);
  private readonly injector = inject(Injector);
  private readonly translate = inject(TranslateService);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);

  /**
   * Whether this view is actually on screen.
   *
   * ★ IT IS PART OF THE SCROLL RULE, NOT A VISIBILITY TOGGLE — the host decides whether to render this
   * component at all. The drawer passes its open state, because closing destroys the scroll container
   * and reopening must land at the newest message with no visible travel; the full page passes the
   * default, because a route that is mounted is a route that is being looked at.
   */
  readonly visible = input(true);

  /**
   * Cap the turns at a reading measure and centre them.
   *
   * ★ AN OPT-IN, NOT A DEFAULT. The drawer is 420px wide — it IS the measure already, and a cap there
   * would be dead CSS. The full page is the one that needed it: the same markup on a wide screen ran
   * lines edge to edge, which is where reading actually falls apart. See the stylesheet.
   */
  readonly wide = input(false);

  readonly draft = signal('');

  /**
   * Which shape the composer has right now — see composer-layout.ts for the rule.
   *
   * ★ IT STARTS STACKED, not as a pill, and that is the fail-safe rather than a preference. Until the
   * mirror has measured something there is no reason to believe the text fits on one line, and the
   * pill is the shape that breaks when that belief is wrong: the button ends up beside — or over —
   * text that needed the width. Starting stacked costs at most one frame of an extra row before the
   * first measurement lands.
   */
  readonly composerLayout = signal<ComposerLayout>('stacked');

  /**
   * The width the button takes away from the field in the PILL state, in px: the 28px control plus the
   * row's gap. Used to measure the text against the NARROW width even while the composer is stacked —
   * which is what stops the two states flip-flopping at the boundary. See composer-layout.ts.
   */
  private static readonly PILL_GUTTER_PX = 34;

  private readonly mirror = new ComposerMirror();

  /**
   * Re-decides the shape from the mirror.
   *
   * ★ FAIL-SAFE IS STACKED, AND THAT IS THE OPPOSITE OF WHAT IT WAS. When the measurement cannot be
   * taken — before the first layout, in a test environment that does no layout, a field not in the DOM
   * yet — the answer is the STACKED shape. Stacked never breaks text: the field owns the whole width
   * and nothing sits beside it. The pill is only correct when we KNOW the text fits on one line, and a
   * default of pill turns every failed measurement into exactly the bug this composer kept shipping,
   * silently. An unnecessary second row is a cosmetic cost; a button on top of the text is not.
   */
  private updateComposerLayout(): void {
    const field = this.composerField()?.nativeElement as HTMLElement | undefined;
    const row = this.composerRow()?.nativeElement as HTMLElement | undefined;

    this.composerCeiling.set(
      composerMaxHeight((this.host.nativeElement as HTMLElement).clientHeight));

    if (!field || !row) {
      this.composerLayout.set('stacked');
      return;
    }

    const pillWidth = this.pillWidthOf(row);
    const contentHeight = this.mirror.measure(field, this.draft(), pillWidth);
    const singleLine = this.mirror.measureSingleLine(field, pillWidth);

    if (contentHeight === null || singleLine === null) {
      this.composerLayout.set('stacked');
      return;
    }

    this.composerLayout.set(composerLayoutFor(contentHeight, singleLine));
  }

  /**
   * The width the field has in the PILL — the row's inner width minus what the inline button takes.
   *
   * Derived from the ROW, not from the field: the field's own width is one of the two things that
   * changes between the states, so measuring it would make the answer depend on the current answer.
   */
  private pillWidthOf(row: HTMLElement): number {
    const style = window.getComputedStyle(row);
    const inner = row.clientWidth
      - Number.parseFloat(style.paddingLeft || '0')
      - Number.parseFloat(style.paddingRight || '0');

    return inner - AssistantConversationComponent.PILL_GUTTER_PX;
  }

  private readonly composerField = viewChild('composerField', { read: ElementRef });

  private readonly composerRow = viewChild<ElementRef<HTMLElement>>('composerRow');

  private readonly host = inject(ElementRef<HTMLElement>);

  /**
   * The composer's height ceiling, recomputed from the panel it is sitting in.
   *
   * ★ MEASURED FROM THE HOST, NOT FROM THE WINDOW. This component is 420px wide in the drawer and the
   * width of the content area on the page, and the two can differ in height by hundreds of pixels. A
   * ceiling read off the viewport would let the composer swallow a short drawer whole. See
   * composerMaxHeight for the floor that keeps the rule usable on a small panel.
   */
  readonly composerCeiling = signal(composerMaxHeight(0));

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
    () => this.store.progressSteps().length >= AssistantConversationComponent.MIN_STEPS_TO_LIST);

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
   * What was on screen last time we looked: whether the view was visible plus which conversation. A
   * change in either means a FRESH view, which must appear already at the bottom rather than scroll
   * there.
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
      const visible = this.visible();
      // Read so the effect re-runs when a turn is appended.
      this.store.messages().length;

      // ★ AND SO IT RE-RUNS ON EVERY FRAGMENT OF THE ANSWER BEING WRITTEN. This read is the whole
      // auto-scroll: while the assistant streams, `messages()` does not change at all — the array gets
      // its new row only when the answer is finished and stored — so an effect watching only that sat
      // still for the entire reply. The text grew below the fold and the user had to chase it by hand,
      // or never saw it. `streamingReply` is what actually changes token by token.
      const streamingLength = this.store.streamingReply()?.length ?? null;

      const view = `${visible}:${conversationId}`;
      // A reopened drawer counts as fresh too: closing destroys the container, and the new one starts
      // at scrollTop 0 with no signal having changed.
      const isFreshView = view !== this.lastView;
      // ★ untracked: isNearBottom() reads the viewChild signal, and depending on it would re-run this
      // effect the moment the container resolves — a second pass that no longer looks "fresh" and so
      // would animate the very jump that must be instant. The user would watch the view travel down
      // from the oldest message, which is the bug this whole rule exists to remove.
      const wasFollowing = untracked(() => this.isNearBottom());
      this.lastView = view;

      if (!visible) {
        return;
      }

      // ★ Do not yank someone out of the history they are reading. A new turn only pulls the view
      // down if they were already at the bottom; a fresh view always starts at the bottom.
      if (!isFreshView && !wasFollowing) {
        return;
      }

      // Opening shows the newest message ALREADY in place — no visible travel from the top. A turn
      // arriving in a conversation you are watching may glide, because you are there to see it.
      //
      // ★ BUT NEVER SMOOTH WHILE STREAMING. A smooth scroll is an animation with a duration, and the
      // next token arrives before it finishes: the animations queue and fight each other, and the text
      // ends up lurching behind the caret instead of following it. Instant per fragment is what reads
      // as "the view is following the writing".
      const streaming = streamingLength !== null;
      const behavior: ScrollBehavior = isFreshView || streaming ? 'auto' : 'smooth';

      afterNextRender(() => this.scrollToBottom(behavior), { injector: this.injector });
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
            () => this.waitingLong.set(true), AssistantConversationComponent.LONG_WAIT_MS);
        }
      });
    });

    // ★ The one-line threshold, taken from the empty field before anything is typed — see
    // `singleLineHeight` for what happened when this was left to the first keystroke.
    afterNextRender(() => this.updateComposerLayout(), { injector: this.injector });

    this.destroyRef.onDestroy(() => {
      this.stopLongWaitClock();
      this.mirror.destroy();
    });
  }

  /** Stops the clock and takes the explanation back down. Safe to call when it was never started. */
  private stopLongWaitClock(): void {
    if (this.longWaitTimer !== null) {
      clearTimeout(this.longWaitTimer);
      this.longWaitTimer = null;
    }

    this.waitingLong.set(false);
  }

  /**
   * False while there are messages below the fold — what shows the "jump to newest" button.
   *
   * ★ IT MIRRORS THE SCROLL RULE RATHER THAN INVENTING A SECOND ONE. The same `NEAR_BOTTOM_PX` slack
   * that decides "was the user following the conversation?" decides whether the button appears, so the
   * button cannot be offering to take someone where they already are — which is what a stricter or
   * looser threshold here would produce, intermittently and only on some browsers.
   *
   * Starts true so nothing flashes on a conversation that does not overflow at all.
   */
  readonly atBottom = signal(true);

  /**
   * The scroll container moved.
   *
   * Bound in the template rather than watched with an observer: the element is right there, the read is
   * three numbers, and it is the same read the scroll rule already does. An IntersectionObserver on a
   * sentinel would need a second source of truth for "at the bottom" and could disagree with the first.
   */
  onScroll(): void {
    this.atBottom.set(this.isNearBottom());
  }

  /** The button: take me to the newest message. */
  jumpToLatest(): void {
    this.scrollToBottom('smooth');
  }

  /** True when the view is at (or within a hair of) the newest message — or when there is nothing yet. */
  private isNearBottom(): boolean {
    const el = this.messagesEl()?.nativeElement;
    if (!el) {
      return true;
    }

    const distance = el.scrollHeight - el.scrollTop - el.clientHeight;
    return distance <= AssistantConversationComponent.NEAR_BOTTOM_PX;
  }

  /** Public so a test can observe the wiring; headless lays out, but not to a scrollable height here. */
  scrollToBottom(behavior: ScrollBehavior = 'auto'): void {
    const el = this.messagesEl()?.nativeElement;
    if (!el) {
      return;
    }

    el.scrollTo({ top: el.scrollHeight, behavior });
    // A smooth scroll reports its position over several frames, so the scroll handler would leave the
    // button up until the animation lands. The intent is known here and now: this ends at the bottom.
    this.atBottom.set(true);
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
   * The assistant answers "how do I create a plan?" with steps and a link to `/plans/new`. Rendered
   * Markdown gives that a plain `<a href>`, and a plain `<a href>` is a FULL PAGE LOAD: Angular is torn
   * down and rebuilt, and the conversation the user was reading — the thing that just told them where
   * to go — is gone at the exact moment they acted on it. The most valuable click in the feature would
   * be the one that destroys it.
   *
   * So internal links are routed instead: `preventDefault` stops the browser, and Angular's Router
   * moves the app underneath, leaving the conversation on screen.
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

  /**
   * Sends the composer's contents — and takes them back if the server never received them.
   *
   * ★ THE BOX IS EMPTIED FIRST, AND THAT IS WHAT MAKES THE RESTORE NECESSARY. Clearing on send is
   * right: the user watches their words leave, and a composer that still held them would invite a
   * second copy of the same question. But when the turn dies BEFORE the server stores it, there is
   * then nothing anywhere — not in the thread, because the user's turn comes from the server, and not
   * in the box. The message the user typed is simply gone, which in a product about money is the last
   * thing that may happen to something somebody wrote.
   *
   * ★ SO IT COMES BACK TO THE COMPOSER, NOT TO THE THREAD. Putting it into `messages` would be a local
   * copy of a turn the server does not have — a second version that a refresh cannot find. In the box
   * it is honest: this is not part of the conversation yet, it is still something you are about to say.
   *
   * ★ AND ONLY INTO AN EMPTY BOX. An answer can take several seconds, and someone who started typing
   * their next question in the meantime must not have it overwritten by the restore.
   */
  async send(): Promise<void> {
    if (!this.canSend()) {
      return;
    }
    const text = this.draft();
    this.draft.set('');
    // The box is empty again, so it is a pill again — the shape follows the content, and sending is
    // the one path that empties it without a keystroke to notice.
    this.updateComposerLayout();
    await this.store.send(text);

    const unsent = this.store.unsentText();
    if (unsent !== null && this.draft().trim().length === 0) {
      this.draft.set(unsent);
      this.composer()?.fill(unsent);
      // Restored text can be long enough to need the stacked shape; decide from it, not from empty.
      this.updateComposerLayout();
    }
  }

  onDraftChange(value: string): void {
    this.draft.set(value);
    this.updateComposerLayout();
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
    this.updateComposerLayout();
  }

  /**
   * Stops the answer being written. See the store — the words already on screen are KEPT, and the
   * composer is usable again immediately, which is the whole reason someone presses this.
   */
  async cancel(): Promise<void> {
    await this.store.cancel();
  }

  /**
   * Re-answers the last failed question. See the store — for a STORED turn it does not re-send the
   * message, because the question is already in the thread.
   *
   * ★ IT ALSO EMPTIES THE COMPOSER WHEN IT IS RETRYING WHAT THE COMPOSER IS HOLDING. A turn that never
   * reached the server was handed back to the box by `send`, so both affordances now offer the same
   * words — and pressing Retry would leave a copy sitting there, ready to be sent a second time.
   * Cleared only when the box still holds EXACTLY what is being retried: if the user edited it, that
   * text is theirs and Retry is not entitled to it.
   */
  async retry(): Promise<void> {
    const retried = this.store.unsentText();
    await this.store.retry();

    if (retried !== null && this.draft() === retried) {
      this.draft.set('');
    }
  }
}
