import { Injectable, inject, signal } from '@angular/core';
import { UI_PREFERENCE_KEYS, UiPreferencesService } from './ui-preferences.service';

/**
 * Opens the welcome modal, from two places that cannot see each other.
 *
 * The modal itself lives in the app shell (it must survive navigation, like the assistant panel), but
 * the "watch it again" button lives inside the /manual screen. A shared signal is what connects them —
 * the same shape AssistantStore uses for the topbar trigger and its panel.
 *
 * ★ "SEEN" BELONGS TO THE USER, ON THE SERVER (KAN-78). It used to be a browser flag, wiped on logout, so
 * switching accounts brought the welcome back every time. It is now a UI preference of the signed-in user.
 *
 * ★ TWO WAYS IN, AND ONLY ONE OF THEM WRITES THE FLAG. Opening automatically is a first-visit event,
 * so closing it records that it happened. Opening it by hand is just re-watching: it must not touch
 * the flag in either direction — not set it (the user might not have seen it automatically yet) and
 * not clear it (re-watching is not "start over").
 */
@Injectable({ providedIn: 'root' })
export class WelcomeService {
  private readonly preferences = inject(UiPreferencesService);

  /** Whether the modal is on screen. */
  readonly isOpen = signal(false);

  /**
   * True when the modal on screen is the automatic first-visit showing — the moment right after
   * registering, which is the one that gets the confetti. Re-watching from /manual is not a celebration.
   */
  readonly celebrate = signal(false);

  /** True while the open one is the automatic first-visit showing, so closing it records the flag. */
  private markSeenOnClose = false;

  /**
   * Called once by the shell. Shows the modal only if this USER has never closed it.
   * ★ Preferences that could not be loaded count as "seen": a welcome on every page load is far worse
   * than a welcome missed once.
   */
  async openIfFirstVisit(): Promise<void> {
    const known = await this.preferences.ensureLoaded();
    if (!known || this.preferences.isFlagSet(UI_PREFERENCE_KEYS.welcomeSeen) || this.isOpen()) return;
    this.markSeenOnClose = true;
    this.celebrate.set(true);
    this.isOpen.set(true);
  }

  /** The /manual "watch it again" button. Never writes the flag — see the class note. */
  openManually(): void {
    this.markSeenOnClose = false;
    this.celebrate.set(false);
    this.isOpen.set(true);
  }

  close(): void {
    if (this.markSeenOnClose) {
      void this.preferences.setFlag(UI_PREFERENCE_KEYS.welcomeSeen);
      this.markSeenOnClose = false;
    }
    this.celebrate.set(false);
    this.isOpen.set(false);
  }
}
