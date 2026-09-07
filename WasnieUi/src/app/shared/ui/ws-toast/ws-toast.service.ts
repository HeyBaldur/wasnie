import { Injectable, signal } from '@angular/core';

export type WsToastType = 'success' | 'error' | 'warning' | 'info';

/**
 * Values interpolated into the toast's translation, by name — the second argument of ngx-translate's
 * `translate` pipe.
 */
export type WsToastParams = Record<string, unknown>;

export interface WsToastItem {
  id: string;
  message: string;
  type: WsToastType;
  /**
   * ★ WHY A TOAST CARRIES PARAMETERS AT ALL. `message` is a translation KEY, and the container pipes
   * it through `translate`. A message with a number in it ("tier 2 ends at 10000") therefore had only
   * two ways to exist: as an English sentence built by whoever called `show` — untranslatable — or as
   * a key with nothing to fill its placeholders. Optional, and undefined for the great majority of
   * toasts, which are static keys.
   */
  params?: WsToastParams;
}

/** How long a toast stays up when nobody is reading it. */
const DEFAULT_DURATION_MS = 4000;

/**
 * The countdown behind one toast.
 *
 * ★★ `remainingMs` IS THE POINT, NOT `handle`. Pausing has to REMEMBER how much time was left, not
 * restart the clock: a toast the reader hovered after three seconds must have one second left when
 * they look away, not a fresh four. Restarting is the easy version and it is wrong in the direction
 * that matters — a long message would keep resetting itself and never leave.
 */
interface WsToastTimer {
  handle: ReturnType<typeof setTimeout> | null;
  /** Milliseconds still owed when the countdown is not running. */
  remainingMs: number;
  /** When the current run started, so the elapsed part can be subtracted on pause. */
  startedAt: number;
}

@Injectable({ providedIn: 'root' })
export class WsToastService {
  readonly toasts = signal<WsToastItem[]>([]);

  private readonly timers = new Map<string, WsToastTimer>();

  show(message: string, type: WsToastType = 'success', params?: WsToastParams): void {
    const id = crypto.randomUUID();
    this.toasts.update(t => [...t, { id, message, type, params }]);

    this.timers.set(id, { handle: null, remainingMs: DEFAULT_DURATION_MS, startedAt: 0 });
    this.run(id);
  }

  /**
   * Hold the countdown while the reader is on the toast.
   *
   * ★★ WHY IT EXISTS: a toast that vanishes mid-sentence is a message that was never delivered. Some
   * of these carry three lines of explanation ("the entry could not be closed — it may have been
   * closed or fixed already"), and four seconds is not long enough to read that and decide what to
   * do. Hovering says "I am reading this"; the toast waits.
   *
   * ★ SAFE TO CALL TWICE. Browsers fire `mouseenter` again after the pointer crosses a child element
   * in some layouts, and a second pause must not subtract the elapsed time twice.
   */
  pause(id: string): void {
    const timer = this.timers.get(id);
    if (!timer || timer.handle === null) return;

    clearTimeout(timer.handle);
    timer.handle = null;
    timer.remainingMs = Math.max(0, timer.remainingMs - (Date.now() - timer.startedAt));
  }

  /** Let it run again with the time it had left. Safe to call on a toast that is already running. */
  resume(id: string): void {
    const timer = this.timers.get(id);
    if (!timer || timer.handle !== null) return;

    this.run(id);
  }

  dismiss(id: string): void {
    const timer = this.timers.get(id);
    if (timer?.handle !== null && timer !== undefined) clearTimeout(timer.handle);
    this.timers.delete(id);

    this.toasts.update(t => t.filter(toast => toast.id !== id));
  }

  private run(id: string): void {
    const timer = this.timers.get(id);
    if (!timer) return;

    timer.startedAt = Date.now();
    timer.handle = setTimeout(() => this.dismiss(id), timer.remainingMs);
  }
}
