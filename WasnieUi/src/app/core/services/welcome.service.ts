import { Injectable, signal } from '@angular/core';

/**
 * The "seen it" flag. Prefixed `wasnie:` like every other key in this app (see TwoFaReminderComponent)
 * — the storage keys deliberately kept the internal name through the Incentra rebrand, because
 * renaming them would orphan the values already in every user's browser.
 */
const SEEN_KEY = 'wasnie:welcome-seen';

/**
 * Opens the welcome modal, from two places that cannot see each other.
 *
 * The modal itself lives in the app shell (it must survive navigation, like the assistant panel), but
 * the "watch it again" button lives inside the /manual screen. A shared signal is what connects them —
 * the same shape AssistantStore uses for the topbar trigger and its panel.
 *
 * ★ TWO WAYS IN, AND ONLY ONE OF THEM WRITES THE FLAG. Opening automatically is a first-visit event,
 * so closing it records that it happened. Opening it by hand is just re-watching: it must not touch
 * the flag in either direction — not set it (the user might not have seen it automatically yet) and
 * not clear it (re-watching is not "start over").
 */
@Injectable({ providedIn: 'root' })
export class WelcomeService {
  /** Whether the modal is on screen. */
  readonly isOpen = signal(false);

  /** True while the open one is the automatic first-visit showing, so closing it records the flag. */
  private markSeenOnClose = false;

  /** True when the user has never closed the automatic welcome on this browser. */
  hasSeen(): boolean {
    try {
      return localStorage.getItem(SEEN_KEY) === '1';
    } catch {
      // Private mode / storage disabled: treat it as "already seen" rather than showing the modal on
      // every single page load, which is far more annoying than never showing it.
      return true;
    }
  }

  /** Called once by the shell. Shows the modal only the first time on this browser. */
  openIfFirstVisit(): void {
    if (this.hasSeen() || this.isOpen()) return;
    this.markSeenOnClose = true;
    this.isOpen.set(true);
  }

  /** The /manual "watch it again" button. Never writes the flag — see the class note. */
  openManually(): void {
    this.markSeenOnClose = false;
    this.isOpen.set(true);
  }

  close(): void {
    if (this.markSeenOnClose) {
      try {
        localStorage.setItem(SEEN_KEY, '1');
      } catch {
        // Storage refused the write. The modal will come back next session; that is the acceptable
        // failure, and it must not throw on the way out of a modal.
      }
      this.markSeenOnClose = false;
    }
    this.isOpen.set(false);
  }
}
