import { computed, inject, Injectable, signal } from '@angular/core';
import { Router } from '@angular/router';
import { Subscription } from 'rxjs';
import { AuthService } from './auth.service';
import { ToastService } from '../../shared/services/toast.service';
import { TabSyncService, TabSyncMessage } from './tab-sync.service';

const IDLE_MS = 28 * 60 * 1000;
const COUNTDOWN_S = 2 * 60;
const ACTIVITY_EVENTS = ['mousedown', 'keydown', 'touchstart', 'scroll', 'mousemove'] as const;
// Throttle cross-tab activity broadcasts: at most once every 5 s to avoid channel flooding.
const ACTIVITY_BROADCAST_INTERVAL_MS = 5_000;

@Injectable({ providedIn: 'root' })
export class InactivityService {
  private readonly authService = inject(AuthService);
  private readonly router = inject(Router);
  private readonly toast = inject(ToastService);
  private readonly tabSync = inject(TabSyncService);

  readonly warningOpen = signal(false);
  readonly countdownSeconds = signal(COUNTDOWN_S);
  readonly countdownDisplay = computed(() => {
    const s = this.countdownSeconds();
    const m = Math.floor(s / 60);
    const sec = s % 60;
    return `${m}:${sec.toString().padStart(2, '0')}`;
  });

  private running = false;
  private idleTimer: ReturnType<typeof setTimeout> | null = null;
  private countdownInterval: ReturnType<typeof setInterval> | null = null;
  private readonly boundReset = () => this.resetTimer();
  private _lastActivityBroadcast = 0;
  private _syncSub: Subscription | null = null;

  start(): void {
    if (this.running) return;
    this.running = true;
    ACTIVITY_EVENTS.forEach(e => document.addEventListener(e, this.boundReset, { passive: true }));
    this.scheduleWarning();
    this._syncSub = this.tabSync.messages$.subscribe(msg => this._handleTabSync(msg));
  }

  stop(): void {
    if (!this.running) return;
    this.running = false;
    ACTIVITY_EVENTS.forEach(e => document.removeEventListener(e, this.boundReset));
    this.clearTimers();
    this.warningOpen.set(false);
    this._syncSub?.unsubscribe();
    this._syncSub = null;
  }

  staySignedIn(): void {
    this.warningOpen.set(false);
    this.clearTimers();
    this.authService.refresh().subscribe({
      next: () => this.scheduleWarning(),
      error: () => this._forceExpire(),
    });
  }

  signOutNow(): void {
    this.warningOpen.set(false);
    this.clearTimers();
    this.authService.forceLogout(false);
    this.router.navigateByUrl('/auth/login');
  }

  private resetTimer(): void {
    if (this.warningOpen()) return;
    this.clearTimers();
    this.scheduleWarning();
    this._broadcastActivity();
  }

  /**
   * Throttled broadcast of user activity to other tabs.
   * Fires at most once per ACTIVITY_BROADCAST_INTERVAL_MS to avoid flooding the channel on
   * high-frequency events (mousemove fires dozens of times per second).
   */
  private _broadcastActivity(): void {
    const now = Date.now();
    if (now - this._lastActivityBroadcast >= ACTIVITY_BROADCAST_INTERVAL_MS) {
      this._lastActivityBroadcast = now;
      this.tabSync.broadcast({ type: 'activity' });
    }
  }

  private _handleTabSync(msg: TabSyncMessage): void {
    switch (msg.type) {
      case 'activity':
        // A tab is active — reset the idle timer and dismiss any open warning.
        this.warningOpen.set(false);
        this.clearTimers();
        this.scheduleWarning();
        break;
      case 'logout':
        // Another tab performed a voluntary logout — silently redirect, no expiry toast.
        this._applyRemoteLogout(false);
        break;
      case 'session-expired':
        // Another tab's session expired — show the expiry toast then redirect.
        this._applyRemoteLogout(true);
        break;
    }
  }

  /**
   * React to a logout/expiry event that originated in another tab.
   * Uses clearSessionSilent() so this tab does NOT re-broadcast the signal.
   */
  private _applyRemoteLogout(showExpiredToast: boolean): void {
    this.stop();
    this.authService.clearSessionSilent();
    if (showExpiredToast) {
      this.toast.show('SESSION.EXPIRED_TOAST', 'error');
    }
    this.router.navigateByUrl('/auth/login');
  }

  private scheduleWarning(): void {
    this.idleTimer = setTimeout(() => this.showWarning(), IDLE_MS);
  }

  private showWarning(): void {
    this.countdownSeconds.set(COUNTDOWN_S);
    this.warningOpen.set(true);
    this.countdownInterval = setInterval(() => {
      const remaining = this.countdownSeconds() - 1;
      if (remaining <= 0) {
        this._forceExpire();
      } else {
        this.countdownSeconds.set(remaining);
      }
    }, 1000);
  }

  private _forceExpire(): void {
    this.warningOpen.set(false);
    this.clearTimers();
    this.authService.forceLogout(true);
    this.toast.show('SESSION.EXPIRED_TOAST', 'error');
    this.router.navigateByUrl('/auth/login');
  }

  private clearTimers(): void {
    if (this.idleTimer !== null) {
      clearTimeout(this.idleTimer);
      this.idleTimer = null;
    }
    if (this.countdownInterval !== null) {
      clearInterval(this.countdownInterval);
      this.countdownInterval = null;
    }
  }
}
