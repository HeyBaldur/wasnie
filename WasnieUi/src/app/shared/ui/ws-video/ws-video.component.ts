import {
  Component,
  ElementRef,
  OnDestroy,
  ViewChild,
  computed,
  effect,
  input,
  signal,
} from '@angular/core';
import { TranslatePipe } from '@ngx-translate/core';
import { IconComponent } from '../../components/icon/icon.component';

/**
 * A silent, looping, chrome-less video — the moving equivalent of an illustration.
 *
 * ★ IT IS DECORATION BY DEFAULT, and every default follows from that. There are no controls because
 * there is nothing to control: no sound, no beginning and no end. It is `aria-hidden` unless the
 * caller passes a label, for the same reason the SVG illustrations are — announcing "video" to a
 * screen reader when the meaning is already in the heading next to it is noise, not access.
 *
 * ★ REDUCED MOTION IS HONOURED, and this is the reason a component exists instead of a `<video>` tag
 * copied into each screen. `prefers-reduced-motion` is set by people who get motion sickness or
 * migraines from looping animation; a decorative clip repeating forever in the corner of an empty
 * state is precisely what it is meant to stop. Those users get the poster frame, held still.
 *
 * ★ WHEN THE CLIP IS THE CONTENT, `playFallback` gives those users a way back in: a Play button that
 * appears ONLY for them. Honouring the preference must not mean locking someone out of the material
 * — the preference says "do not start moving things at me", not "never let me watch anything". It is
 * opt-in precisely so decorative loops do not sprout a button inviting people to play scenery. And
 * when it is used, the clip plays ONCE: they consented to watch it, not to have it repeat forever.
 *
 * ★ AND `[autoplay]="false"` TURNS THE WHOLE THING INTO A PLAYER, for the case where the clip is not
 * scenery at all but something a person sits down and watches. It holds the first frame, puts the
 * Play button in the middle and waits — the same "you consented to watch it" rule as above, so it
 * plays once rather than looping. The two paths deliberately share ONE button: a primitive with two
 * different play affordances depending on why it is not playing would be a primitive nobody trusts.
 *
 * ★ MUTED IS LOAD-BEARING, not just polite: browsers refuse to autoplay a video with sound, so an
 * unmuted clip here would silently never start. `muted` is set as a DOM PROPERTY in code as well as
 * an attribute, because Angular templates do not reliably reflect the attribute to the property.
 */
@Component({
  selector: 'ws-video',
  standalone: true,
  imports: [TranslatePipe, IconComponent],
  template: `
    <video
      #video
      class="ws-video__el"
      [style.object-fit]="fit()"
      [src]="resolvedSrc()"
      [poster]="poster() || null"
      [attr.aria-label]="ariaLabel() || null"
      [attr.aria-hidden]="ariaLabel() ? null : 'true'"
      [attr.role]="ariaLabel() ? 'img' : null"
      [loop]="shouldAutoplay()"
      [muted]="true"
      [controls]="false"
      [autoplay]="shouldAutoplay()"
      playsinline
      disablepictureinpicture
      [attr.preload]="preload()"
      tabindex="-1"
      (play)="onPlay()"
      (playing)="buffering.set(false)"
      (waiting)="buffering.set(true)"
      (pause)="onStop()"
      (ended)="onStop()"
    ></video>

    @if (showControl()) {
      <button
        type="button"
        class="ws-video__control"
        [class.ws-video__control--playing]="playing()"
        [class.ws-video__control--buffering]="buffering()"
        [attr.aria-label]="(playing() ? 'COMMON.PAUSE_VIDEO' : 'COMMON.PLAY_VIDEO') | translate"
        (click)="toggle()"
      >
        <span class="ws-video__disc">
          @if (buffering()) {
            <app-icon name="loader" [size]="30" />
          } @else {
            <app-icon [name]="playing() ? 'pause' : 'play'" [size]="30" />
          }
        </span>
      </button>
    }
  `,
  styles: [
    `
      :host {
        display: block;
        line-height: 0;
        position: relative;
      }

      .ws-video__el {
        display: block;
        width: 100%;
        height: 100%;
      }

      /* ★ THE CONTROL IS THE WHOLE CLIP, NOT JUST THE DISC — that is what makes it behave like the
         native one. Clicking anywhere on the picture toggles playback, and because the button is
         always mounted while the clip is a player, there is no state in which the viewer is left
         watching something they cannot stop. The earlier version unmounted the button on play: a
         thirty-second clip with no way to pause it and nothing to click.

         NOTE: no backticks in these comments — this whole block is a template literal, and one would
         close it. */
      .ws-video__control {
        position: absolute;
        inset: 0;
        width: 100%;
        height: 100%;
        display: flex;
        align-items: center;
        justify-content: center;
        padding: 0;
        border: 0;
        background: none;
        cursor: pointer;
      }

      /* The disc: centred by the flex container above, so it needs no offsets of its own. */
      .ws-video__disc {
        display: flex;
        align-items: center;
        justify-content: center;
        width: var(--space-16);
        height: var(--space-16);
        border-radius: var(--radius-full);
        background: var(--color-brand);
        color: var(--color-brand-contrast);
        box-shadow: var(--shadow-lg);
        transition:
          opacity var(--transition-normal),
          background-color var(--transition-base),
          transform var(--transition-base);
      }

      /* ★ IT GETS OUT OF THE WAY WHILE THE CLIP RUNS, AND COMES BACK ON APPROACH — again the native
         behaviour. Fading the DISC and not the button is deliberate: the click target stays the full
         picture the whole time, so a viewer who just wants to pause can hit anywhere, and the pause
         glyph appears the moment the pointer or the keyboard arrives. */
      .ws-video__control--playing .ws-video__disc {
        opacity: 0;
      }

      /* ★ EXCEPT WHILE IT IS FETCHING. Pressing Play on a clip that has only loaded its metadata is
         the one moment where hiding the control would be a lie: nothing is moving yet, and a viewer
         staring at a still poster with no indicator concludes the button is broken and presses it
         again — which pauses it. The spinner is the answer the native control gives, and this rule is
         what keeps it on screen to give it. */
      .ws-video__control--buffering .ws-video__disc {
        opacity: 1;
      }

      /* A loading indicator is feedback, not decoration, which is why it still turns under
         prefers-reduced-motion: it is the one moving thing those viewers are meant to see, and a
         frozen spinner reads as a hang. */
      .ws-video__control--buffering app-icon {
        animation: ws-video-spin 900ms linear infinite;
      }

      @keyframes ws-video-spin {
        to {
          transform: rotate(360deg);
        }
      }

      .ws-video__control:hover .ws-video__disc,
      .ws-video__control:focus-visible .ws-video__disc {
        opacity: 1;
        background: var(--color-brand-hover);
        transform: scale(1.06);
      }

      .ws-video__control:active .ws-video__disc {
        transform: scale(1);
      }

      .ws-video__control:focus-visible {
        outline: none;
      }

      .ws-video__control:focus-visible .ws-video__disc {
        box-shadow: var(--shadow-lg), var(--shadow-focus);
      }

      /* The play triangle's mass sits left of the glyph's own centre (it spans x 7→20 in a 24 box), so
         a disc that is mathematically centred still reads as off-centre. One pixel puts it right — and
         only for the triangle: the pause bars are symmetrical and must not move. */
      .ws-video__disc app-icon {
        display: block;
      }

      .ws-video__control:not(.ws-video__control--playing) .ws-video__disc app-icon {
        transform: translateX(1px);
      }
    `,
  ],
})
export class WsVideoComponent implements OnDestroy {
  /** Path to the clip, e.g. "/videos/quotas.mp4" (files live in `public/videos`). */
  readonly src = input.required<string>();

  /**
   * Still frame shown before playback — what reduced-motion users see until they press Play, and what
   * a player shows while its clip has not been asked for.
   *
   * ★ WORTH SUPPLYING FOR ANY PLAYER, and not for looks: it is what lets `preload` stay at `metadata`
   * (see below), so the megabytes are spent by the people who press Play rather than by everyone who
   * opens the screen.
   */
  readonly poster = input('');

  /**
   * How the clip fills its box. `cover` (the default) suits a framed slot whose ratio matches the
   * clip; `contain` suits a box of unknown shape, where cropping would eat the artwork.
   *
   * ★ It is an INPUT rather than something the caller styles, because it cannot be styled from
   * outside: the <video> lives in this template and carries this component's encapsulation attribute,
   * so a parent rule targeting `video` matches nothing and fails silently.
   */
  readonly fit = input<'cover' | 'contain'>('cover');

  /**
   * Leave empty for decoration (the default): the element is hidden from assistive technology.
   * Set it only when the clip carries meaning the surrounding copy does not.
   */
  readonly ariaLabel = input('');

  /**
   * Set to true when the clip IS the content rather than scenery. Adds a Play button for
   * reduced-motion users only — see the class note on why it is opt-in.
   */
  readonly playFallback = input(false);

  /**
   * Set to false when the clip is something a person WATCHES rather than scenery that moves behind
   * the content. It holds the first frame, shows the Play button and starts only when pressed — and
   * then plays once, because a clip somebody chose to watch has an end.
   */
  readonly autoplay = input(true);

  @ViewChild('video') private videoRef?: ElementRef<HTMLVideoElement>;

  protected readonly playing = signal(false);

  /** Playback was asked for but no picture is moving yet — the clip is fetching. */
  protected readonly buffering = signal(false);

  private readonly motionQuery: MediaQueryList | null =
    typeof matchMedia === 'function' ? matchMedia('(prefers-reduced-motion: reduce)') : null;

  /** A signal, not a constant: the preference can be switched mid-session and must take effect now. */
  protected readonly reducedMotion = signal(this.motionQuery?.matches ?? false);

  /** Autoplay is the caller's intent AND the viewer's preference — either one can veto it. */
  protected readonly shouldAutoplay = computed(() => this.autoplay() && !this.reducedMotion());

  /**
   * How much of the clip to fetch before anyone asks for it. There are three cases and only one of
   * them is expensive.
   *
   * ★ `metadata` FETCHES NO PICTURE — that is the fact the whole thing turns on. It gets duration and
   * dimensions, `readyState` stops at 1, and painting a frame needs 2. So:
   *
   * - Autoplaying (decoration): `metadata`. It is about to stream anyway.
   * - A player WITH a poster: `metadata`. The poster is the picture; the clip can wait until Play is
   *   pressed, which is the difference between a modal costing 12 KB and costing 4.6 MB for viewers
   *   who never press it.
   * - A player with NO poster: `auto`, because downloading it is then the ONLY way to have anything
   *   on screen. This is the expensive case, and giving the caller a poster is how to leave it.
   */
  protected readonly preload = computed<'metadata' | 'auto'>(() =>
    this.shouldAutoplay() || this.poster() ? 'metadata' : 'auto'
  );

  /**
   * ★ THE `#t=` FRAGMENT IS THE POSTER'S UNDERSTUDY. A paused video with no poster is entitled to
   * paint nothing, and the modal's black rectangle with a Play button floating on it was exactly
   * that. The fragment asks the browser to seek to 0.1s and show that frame instead — which only
   * works together with the `auto` above, since a frame has to be downloaded before it can be shown.
   *
   * Skipped when the clip autoplays (it is about to paint regardless), when the caller supplied a
   * real poster (better in every way: it costs kilobytes, it can be a chosen frame rather than
   * whatever happens to be at 0.1s, and it needs no download of the clip at all), and when the caller
   * already wrote their own fragment.
   */
  protected readonly resolvedSrc = computed(() => {
    const src = this.src();
    const needsFrame = !this.autoplay() && !this.poster() && !src.includes('#');
    return needsFrame ? `${src}#t=0.1` : src;
  });

  /**
   * Whether this clip is something the viewer drives — a player — rather than scenery.
   *
   * ★ IT DOES NOT DEPEND ON `playing`, AND THAT IS THE WHOLE FIX. It used to, so the control vanished
   * the instant the clip started: thirty seconds of video with no pause, nothing to click, and if the
   * end was never reached (modal closed and reopened, a browser that fires no `ended`) no way back to
   * Play either. A control that disappears while the thing it controls is running is not a control.
   * Once the clip is a player it stays a player; only the GLYPH changes.
   */
  protected readonly showControl = computed(
    () => !this.autoplay() || (this.playFallback() && this.reducedMotion())
  );

  private readonly onMotionPreferenceChange = (event: MediaQueryListEvent): void => {
    this.reducedMotion.set(event.matches);

    const el = this.videoRef?.nativeElement;
    if (!el) return;

    // Turning reduced motion ON mid-session must stop the loop NOW, not on the next page load.
    if (event.matches) {
      el.pause();
      el.currentTime = 0;
    } else if (this.autoplay()) {
      // Turning it OFF only restarts clips that were meant to run on their own; a player the viewer
      // has not pressed must stay where it is.
      this.tryPlay(el);
    }
  };

  constructor() {
    this.motionQuery?.addEventListener('change', this.onMotionPreferenceChange);

    // `muted` must be a property, not only an attribute, or the browser blocks autoplay. Re-applied
    // whenever the source changes, since a new src resets the element.
    effect(() => {
      this.src();
      queueMicrotask(() => {
        const el = this.videoRef?.nativeElement;
        if (!el) return;
        el.muted = true;
        if (this.shouldAutoplay()) this.tryPlay(el);
      });
    });
  }

  /**
   * Playback was requested.
   *
   * `readyState < HAVE_FUTURE_DATA` means the browser cannot yet draw the next frame, so this is a
   * request that will have to wait — which is exactly when the spinner belongs. Checked here rather
   * than relying on `waiting` alone, because a clip that has never been fetched (a player preloading
   * only its metadata, which is the whole point of having a poster) can sit on the request for a
   * while before it fires anything at all.
   */
  protected onPlay(): void {
    this.playing.set(true);
    const el = this.videoRef?.nativeElement;
    if (el && el.readyState < 3) this.buffering.set(true);
  }

  /** Paused or finished: nothing is playing and nothing is being waited for. */
  protected onStop(): void {
    this.playing.set(false);
    this.buffering.set(false);
  }

  /**
   * The control: play when stopped, pause when running — the same contract as the native button.
   *
   * ★ IT ASKS THE ELEMENT, NOT THE SIGNAL. `playing` is updated FROM the media events, so it is a
   * report of what happened, not the source of truth; deciding from it would let the two disagree
   * after any state change we did not originate (a browser pausing a backgrounded tab, playback
   * refused, the clip reaching its end between a render and a click).
   */
  toggle(): void {
    const el = this.videoRef?.nativeElement;
    if (!el) return;

    if (!el.paused && !el.ended) {
      el.pause();
      return;
    }

    // A clip that ran to the end restarts rather than sitting on its last frame doing nothing.
    if (el.ended) el.currentTime = 0;
    el.muted = true;
    this.tryPlay(el);
  }

  private tryPlay(el: HTMLVideoElement): void {
    void el.play().catch(() => {
      // Autoplay can still be refused (power saving, background tab). A still frame is a fine outcome
      // for decoration, and the Play button stays on screen for the case that needs it — never
      // surface this to the user.
    });
  }

  ngOnDestroy(): void {
    this.motionQuery?.removeEventListener('change', this.onMotionPreferenceChange);
  }
}
