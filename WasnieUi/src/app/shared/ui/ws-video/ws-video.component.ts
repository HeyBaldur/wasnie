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
 * ★ MUTED IS LOAD-BEARING, not just polite: browsers refuse to autoplay a video with sound, so an
 * unmuted clip here would silently never start. `muted` is set as a DOM PROPERTY in code as well as
 * an attribute, because Angular templates do not reliably reflect the attribute to the property.
 */
@Component({
  selector: 'ws-video',
  standalone: true,
  imports: [TranslatePipe],
  template: `
    <video
      #video
      class="ws-video__el"
      [style.object-fit]="fit()"
      [src]="src()"
      [poster]="poster() || null"
      [attr.aria-label]="ariaLabel() || null"
      [attr.aria-hidden]="ariaLabel() ? null : 'true'"
      [attr.role]="ariaLabel() ? 'img' : null"
      [loop]="!reducedMotion()"
      [muted]="true"
      [controls]="false"
      [autoplay]="!reducedMotion()"
      playsinline
      disablepictureinpicture
      preload="metadata"
      tabindex="-1"
      (play)="playing.set(true)"
      (pause)="playing.set(false)"
      (ended)="playing.set(false)"
    ></video>

    @if (showPlayButton()) {
      <button type="button" class="ws-video__play" (click)="play()">
        <span class="ws-video__play-icon" aria-hidden="true">
          <svg width="22" height="22" viewBox="0 0 24 24" fill="currentColor">
            <path d="M8 5v14l11-7z" />
          </svg>
        </span>
        <span class="ws-video__play-label">{{ 'COMMON.PLAY_VIDEO' | translate }}</span>
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

      /* Centred over the still frame — the only affordance these users get, so it is a real labelled
         button and not a bare glyph. */
      .ws-video__play {
        position: absolute;
        inset: 0;
        margin: auto;
        width: fit-content;
        height: fit-content;
        display: flex;
        align-items: center;
        gap: var(--space-2);
        padding: var(--space-2) var(--space-4);
        border: 1px solid var(--color-border-default);
        border-radius: var(--radius-full);
        background: var(--color-bg-surface);
        color: var(--color-text-primary);
        font-size: var(--font-size-13);
        font-weight: 600;
        line-height: 1.2;
        cursor: pointer;
        box-shadow: var(--shadow-md);
        transition: background-color 150ms ease, border-color 150ms ease;
      }

      .ws-video__play:hover {
        background: var(--color-bg-surface-raised);
        border-color: var(--color-brand);
        color: var(--color-brand);
      }

      .ws-video__play-icon {
        display: flex;
        align-items: center;
        color: var(--color-brand);
      }
    `,
  ],
})
export class WsVideoComponent implements OnDestroy {
  /** Path to the clip, e.g. "/videos/quotas.mp4" (files live in `public/videos`). */
  readonly src = input.required<string>();

  /** Still frame shown before playback — and what reduced-motion users see until they press Play. */
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

  @ViewChild('video') private videoRef?: ElementRef<HTMLVideoElement>;

  protected readonly playing = signal(false);

  private readonly motionQuery: MediaQueryList | null =
    typeof matchMedia === 'function' ? matchMedia('(prefers-reduced-motion: reduce)') : null;

  /** A signal, not a constant: the preference can be switched mid-session and must take effect now. */
  protected readonly reducedMotion = signal(this.motionQuery?.matches ?? false);

  protected readonly showPlayButton = computed(
    () => this.playFallback() && this.reducedMotion() && !this.playing()
  );

  private readonly onMotionPreferenceChange = (event: MediaQueryListEvent): void => {
    this.reducedMotion.set(event.matches);

    const el = this.videoRef?.nativeElement;
    if (!el) return;

    // Turning reduced motion ON mid-session must stop the loop NOW, not on the next page load.
    if (event.matches) {
      el.pause();
      el.currentTime = 0;
    } else {
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
        if (!this.reducedMotion()) this.tryPlay(el);
      });
    });
  }

  /** The Play button. Only reachable when the fallback is enabled and motion is reduced. */
  play(): void {
    const el = this.videoRef?.nativeElement;
    if (!el) return;
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
