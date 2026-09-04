import { DestroyRef, inject, signal } from '@angular/core';

/** Where the open menu is pinned, in viewport coordinates. Either `top` or `bottom` is set, never both. */
export interface RowMenuPosition {
  readonly top?: number;
  readonly bottom?: number;
  readonly right: number;
}

/**
 * How little room below the trigger forces the menu to open upward instead. Roughly the tallest menu
 * these lists carry, so a menu near the pagination bar flips rather than being clipped.
 */
const FLIP_THRESHOLD_PX = 108;

/** The gap between the trigger and the menu. */
const OFFSET_PX = 4;

/**
 * The "⋯" menu on a table row.
 *
 * ★★ IT FOLLOWS ITS TRIGGER WHILE THE PAGE SCROLLS, which is the whole reason it exists. The menu is
 * `position: fixed`, so its coordinates are relative to the VIEWPORT: measured once when it opens,
 * they are stale the instant anything scrolls, and the menu hangs in mid-screen while the row it
 * belongs to slides away underneath. Four list screens each had their own copy of the measure-once
 * version — quotas, payees, plans and assignments — so the same defect was reported four times over.
 *
 * ★★ THE LISTENER IS ON `window` IN THE CAPTURE PHASE, AND BOTH HALVES MATTER. Scroll events do not
 * bubble, and in this app the page does not scroll in `window` at all — it scrolls inside app-shell's
 * `main.shell__content`. A `window:scroll` or `document:scroll` listener in the bubble phase therefore
 * never fires here, which is exactly what makes this bug look unfixable from the outside. Capture sees
 * scrolls from every nested container.
 *
 * ★ `menuPosition` IS A SIGNAL, and that is load-bearing rather than stylistic — the same trap the
 * date-picker documents. The template binds through it, so repositioning that wrote to a plain field
 * would run correctly, measure correctly, and change nothing on screen.
 *
 * ★ SCROLLING DOES NOT CLOSE IT. A dropdown that vanishes on the first wheel tick is its own annoyance;
 * an outside click closes it, as it did before. The one exception is a trigger that has scrolled out of
 * the viewport, where staying open would leave the menu floating over unrelated rows.
 */
export class RowMenuController {
  readonly openMenuId = signal<string | null>(null);
  readonly menuPosition = signal<RowMenuPosition | null>(null);

  /** The button the open menu belongs to — kept so the menu can be re-measured against it later. */
  private trigger: HTMLElement | null = null;
  private repositionScheduled = false;

  constructor(destroyRef: DestroyRef) {
    window.addEventListener('scroll', this.onViewportChange, { capture: true, passive: true });
    window.addEventListener('resize', this.onViewportChange, { passive: true });

    destroyRef.onDestroy(() => {
      window.removeEventListener('scroll', this.onViewportChange, { capture: true } as EventListenerOptions);
      window.removeEventListener('resize', this.onViewportChange);
    });
  }

  toggle(id: string, event: Event): void {
    event.stopPropagation();

    const isOpening = this.openMenuId() !== id;
    this.openMenuId.update(cur => (cur === id ? null : id));

    if (!isOpening) {
      this.close();
      return;
    }

    // ★ Captured synchronously: `currentTarget` is only set while the event is being dispatched, and
    // is null by the time any listener below would read it.
    this.trigger = event.currentTarget as HTMLElement;
    this.place();
  }

  close(): void {
    this.openMenuId.set(null);
    this.menuPosition.set(null);
    this.trigger = null;
  }

  /** Measure the trigger and pin the menu to it. Safe to call as often as the viewport changes. */
  private place(): void {
    const trigger = this.trigger;
    if (!trigger) return;

    const rect = trigger.getBoundingClientRect();

    // Out of sight: the row is gone, so the menu has nothing left to belong to.
    if (rect.bottom < 0 || rect.top > window.innerHeight) {
      this.close();
      return;
    }

    const right = window.innerWidth - rect.right;
    const flipUp = window.innerHeight - rect.bottom < FLIP_THRESHOLD_PX;

    this.menuPosition.set(flipUp
      ? { bottom: window.innerHeight - rect.top + OFFSET_PX, right }
      : { top: rect.bottom + OFFSET_PX, right });
  }

  /**
   * ★ Coalesced to one measurement per frame. Scroll fires far faster than the screen repaints, and
   * `getBoundingClientRect()` forces layout — measuring on every event would make the very scroll the
   * user is performing stutter.
   */
  private readonly onViewportChange = (): void => {
    if (this.openMenuId() === null || this.repositionScheduled) return;

    this.repositionScheduled = true;
    requestAnimationFrame(() => {
      this.repositionScheduled = false;
      if (this.openMenuId() !== null) this.place();
    });
  };
}

/** Create a {@link RowMenuController} from an injection context (a component field initializer). */
export function createRowMenu(): RowMenuController {
  return new RowMenuController(inject(DestroyRef));
}
