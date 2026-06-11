import { inject, Injectable, NgZone, OnDestroy } from '@angular/core';
import { Subject } from 'rxjs';

export type TabSyncMessage =
  | { type: 'activity' }
  | { type: 'logout' }
  | { type: 'session-expired' };

const CHANNEL_NAME = 'wasnie-session';
const LS_FALLBACK_KEY = 'wasnie:tab-sync';
const VALID_TYPES = new Set<string>(['activity', 'logout', 'session-expired']);

/**
 * Cross-tab session synchronisation via BroadcastChannel (same-origin scoped by spec).
 * Falls back to localStorage storage events for environments without BroadcastChannel.
 *
 * SECURITY NOTE: only signal types are transmitted — auth tokens are never sent over the channel.
 * Real security enforcement stays on the backend (expired token → 401).
 */
@Injectable({ providedIn: 'root' })
export class TabSyncService implements OnDestroy {
  private readonly zone = inject(NgZone);

  private readonly _message$ = new Subject<TabSyncMessage>();
  readonly messages$ = this._message$.asObservable();

  private readonly _channel: BroadcastChannel | null;
  private readonly _useFallback: boolean;

  constructor() {
    if (typeof BroadcastChannel !== 'undefined') {
      this._channel = new BroadcastChannel(CHANNEL_NAME);
      // BroadcastChannel fires outside Angular's zone; run() re-enters it so signals and CD work.
      this._channel.onmessage = (e: MessageEvent<TabSyncMessage>) => {
        this.zone.run(() => this._message$.next(e.data));
      };
      this._useFallback = false;
    } else {
      this._channel = null;
      this._useFallback = true;
      window.addEventListener('storage', this._onStorage);
    }
  }

  /** Broadcast a signal to every other tab of the same origin. Never includes auth tokens. */
  broadcast(message: TabSyncMessage): void {
    if (this._channel) {
      this._channel.postMessage(message);
    } else if (this._useFallback) {
      // Writing localStorage triggers the 'storage' event in all other same-origin tabs.
      localStorage.setItem(LS_FALLBACK_KEY, JSON.stringify({ ...message, _t: Date.now() }));
    }
  }

  private readonly _onStorage = (e: StorageEvent): void => {
    if (e.key !== LS_FALLBACK_KEY || !e.newValue) return;
    try {
      const data = JSON.parse(e.newValue) as { type?: string };
      if (!data.type || !VALID_TYPES.has(data.type)) return;
      this.zone.run(() =>
        this._message$.next({ type: data.type as TabSyncMessage['type'] }),
      );
    } catch { /* ignore malformed entries */ }
  };

  ngOnDestroy(): void {
    this._channel?.close();
    this._message$.complete();
    if (this._useFallback) {
      window.removeEventListener('storage', this._onStorage);
    }
  }
}
