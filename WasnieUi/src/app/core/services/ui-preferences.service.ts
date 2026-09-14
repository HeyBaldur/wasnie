import { HttpClient } from '@angular/common/http';
import { Injectable, computed, effect, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { environment } from '../../../environments/environment';
import { AuthService } from './auth.service';

/**
 * Every preference key the app uses — one place, so two features cannot pick the same key by accident. A new modal
 * or notice that must remember a choice adds a line here and uses {@link UiPreferencesService}; nothing else.
 */
export const UI_PREFERENCE_KEYS = {
  welcomeSeen: 'welcome-seen',
  sandboxIntroDismissed: 'sandbox-intro-dismissed',
  twoFaReminder: 'twofa-reminder',
} as const;

export type UiPreferenceKey = (typeof UI_PREFERENCE_KEYS)[keyof typeof UI_PREFERENCE_KEYS];

/**
 * The browser flags these preferences replace (KAN-78). Read ONCE after the first load and copied to the server if the
 * server has nothing yet, so nobody sees a modal again just because this change shipped; then removed.
 */
const LEGACY_KEYS: ReadonlyArray<{ storage: string; key: UiPreferenceKey; convert: (raw: string) => string | null }> = [
  { storage: 'wasnie:welcome-seen', key: UI_PREFERENCE_KEYS.welcomeSeen, convert: raw => (raw === '1' ? 'true' : null) },
  { storage: 'wasnie:sandbox-intro-dismissed', key: UI_PREFERENCE_KEYS.sandboxIntroDismissed, convert: raw => (raw === '1' ? 'true' : null) },
  { storage: 'wasnie:2fa-reminder-dismissed', key: UI_PREFERENCE_KEYS.twoFaReminder, convert: raw => (raw === 'true' ? JSON.stringify({ state: 'dismissed' }) : null) },
  {
    storage: 'wasnie:2fa-reminder-snooze',
    key: UI_PREFERENCE_KEYS.twoFaReminder,
    convert: raw => {
      const snoozedAt = Number.parseInt(raw, 10);
      return Number.isFinite(snoozedAt)
        ? JSON.stringify({ state: 'snoozed', until: new Date(snoozedAt + 3 * 24 * 60 * 60 * 1000).toISOString() })
        : null;
    },
  },
];

/**
 * The signed-in user's UI preferences, stored on the server (KAN-78): "saw the welcome", "dismissed the sandbox
 * intro", "snoozed the 2FA reminder until …".
 *
 * ★ PER USER, NOT PER BROWSER. localStorage did not know who was signed in and was wiped on logout on purpose, so
 * every modal reappeared after switching users, machines or browsers. The server keeps the choice with the person.
 *
 * ★ UNKNOWN IS NOT "NOT SEEN". Until the load succeeds nothing is known, and callers must not show a first-run modal
 * on a guess: {@link ensureLoaded} resolves false when the load failed, and a modal that cannot confirm it is owed
 * stays closed — seeing a welcome twice is worse than missing it once.
 *
 * ★ RESET WHEN THE USER CHANGES, so the next account never inherits the previous one's answers.
 */
@Injectable({ providedIn: 'root' })
export class UiPreferencesService {
  private readonly http = inject(HttpClient);
  private readonly auth = inject(AuthService);
  private readonly base = `${environment.apiBaseUrl}/me/ui-preferences`;

  /** null = not loaded (or loading) for the current user. */
  private readonly values = signal<Record<string, string> | null>(null);
  private inFlight: Promise<boolean> | null = null;
  private loadedForUser: string | null = null;

  readonly loaded = computed(() => this.values() !== null);

  constructor() {
    effect(() => {
      const userId = this.auth.currentUser()?.userId ?? null;
      if (userId !== this.loadedForUser) {
        this.loadedForUser = userId;
        this.inFlight = null;
        this.values.set(null);
      }
    });
  }

  /** Loads once per user. Resolves true when the preferences are known, false when they could not be read. */
  ensureLoaded(): Promise<boolean> {
    if (this.values() !== null) return Promise.resolve(true);
    if (!this.auth.isAuthenticated()) return Promise.resolve(false);
    if (this.inFlight) return this.inFlight;

    const userId = this.auth.currentUser()?.userId ?? null;
    this.inFlight = firstValueFrom(this.http.get<Record<string, string>>(this.base))
      .then(values => {
        // The user changed while the request was out: these answers belong to somebody else.
        if ((this.auth.currentUser()?.userId ?? null) !== userId) return false;
        this.values.set({ ...values });
        this.adoptLegacyBrowserFlags();
        return true;
      })
      .catch(() => {
        this.inFlight = null;
        return false;
      });
    return this.inFlight;
  }

  get(key: UiPreferenceKey): string | null {
    return this.values()?.[key] ?? null;
  }

  isFlagSet(key: UiPreferenceKey): boolean {
    return this.get(key) === 'true';
  }

  /**
   * Remembers a choice. Applied locally at once (the modal closes now), then saved. A failed save keeps the choice
   * for this session only — the acceptable failure; it must never throw on the way out of a modal.
   */
  set(key: UiPreferenceKey, value: string): Promise<void> {
    this.values.update(current => ({ ...(current ?? {}), [key]: value }));
    return firstValueFrom(this.http.put<void>(`${this.base}/${key}`, { value }))
      .then(() => undefined)
      .catch(() => undefined);
  }

  setFlag(key: UiPreferenceKey): Promise<void> {
    return this.set(key, 'true');
  }

  private adoptLegacyBrowserFlags(): void {
    for (const legacy of LEGACY_KEYS) {
      let raw: string | null = null;
      try {
        raw = localStorage.getItem(legacy.storage);
        if (raw !== null) localStorage.removeItem(legacy.storage);
      } catch {
        continue;
      }
      if (raw === null || this.get(legacy.key) !== null) continue;
      const value = legacy.convert(raw);
      if (value !== null) void this.set(legacy.key, value);
    }
  }
}

/** The 2FA reminder's remembered choice. */
export type TwoFaReminderPreference = { state: 'dismissed' } | { state: 'snoozed'; until: string };

/** Whether the 2FA reminder is currently suppressed by the user's choice. Unreadable values suppress nothing. */
export function isTwoFaReminderSuppressed(raw: string | null, now: Date = new Date()): boolean {
  if (!raw) return false;
  try {
    const pref = JSON.parse(raw) as Partial<{ state: string; until: string }>;
    if (pref.state === 'dismissed') return true;
    if (pref.state === 'snoozed' && pref.until) {
      const until = new Date(pref.until).getTime();
      return Number.isFinite(until) && now.getTime() < until;
    }
  } catch {
    // A value this build cannot read is treated as no choice made.
  }
  return false;
}
