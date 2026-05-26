import { computed, inject, Injectable, signal } from '@angular/core';
import { Router } from '@angular/router';
import { AuthService } from './auth.service';
import { ToastService } from '../../shared/services/toast.service';

const IDLE_MS = 28 * 60 * 1000;
const COUNTDOWN_S = 2 * 60;
const ACTIVITY_EVENTS = ['mousedown', 'keydown', 'touchstart', 'scroll', 'mousemove'] as const;

@Injectable({ providedIn: 'root' })
export class InactivityService {
  private readonly authService = inject(AuthService);
  private readonly router = inject(Router);
  private readonly toast = inject(ToastService);

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

  start(): void {
    if (this.running) return;
    this.running = true;
    ACTIVITY_EVENTS.forEach(e => document.addEventListener(e, this.boundReset, { passive: true }));
    this.scheduleWarning();
  }

  stop(): void {
    if (!this.running) return;
    this.running = false;
    ACTIVITY_EVENTS.forEach(e => document.removeEventListener(e, this.boundReset));
    this.clearTimers();
    this.warningOpen.set(false);
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
