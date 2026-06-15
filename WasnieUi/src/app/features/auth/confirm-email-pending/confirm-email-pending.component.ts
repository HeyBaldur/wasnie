import { Component, OnInit, OnDestroy, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { TranslatePipe } from '@ngx-translate/core';
import { CurrentUserService } from '../../../core/auth/current-user.service';
import { AuthService } from '../../../core/services/auth.service';
import { Router } from '@angular/router';
import { WsButtonComponent } from '../../../shared/ui';

const RESEND_COOLDOWN_SECONDS = 120; // mirrors backend 2-minute cooldown

@Component({
  selector: 'app-confirm-email-pending',
  standalone: true,
  imports: [TranslatePipe, WsButtonComponent],
  templateUrl: './confirm-email-pending.component.html',
  styleUrl: './confirm-email-pending.component.scss',
})
export class ConfirmEmailPendingComponent implements OnInit, OnDestroy {
  private readonly http = inject(HttpClient);
  private readonly currentUser = inject(CurrentUserService);
  private readonly authService = inject(AuthService);
  private readonly router = inject(Router);

  readonly email = signal('');
  readonly resending = signal(false);
  readonly resent = signal(false);
  readonly error = signal<string | null>(null);
  readonly cooldownSeconds = signal(0);

  private cooldownTimer: ReturnType<typeof setInterval> | null = null;

  ngOnInit(): void {
    const stored = sessionStorage.getItem('wasnie:confirm-email');
    const fromUser = this.currentUser.currentUser()?.email;
    const resolved = stored ?? fromUser ?? '';

    if (!resolved) {
      void this.router.navigateByUrl('/auth/login');
      return;
    }

    // If the user's own session shows their email is already confirmed, don't lie to them.
    // Detection is session-based (own data only) — no enumeration risk.
    if (this.currentUser.currentUser()?.emailConfirmed) {
      sessionStorage.removeItem('wasnie:confirm-email');
      void this.router.navigateByUrl('/auth/login?alreadyConfirmed=true');
      return;
    }

    this.email.set(resolved);
    // Keep sessionStorage in sync so F5 works
    sessionStorage.setItem('wasnie:confirm-email', resolved);
  }

  ngOnDestroy(): void {
    this.clearTimer();
  }

  resend(): void {
    const email = this.email();
    if (this.resending() || !email || this.cooldownSeconds() > 0) return;
    this.resending.set(true);
    this.error.set(null);
    this.http.post('/api/auth/resend-confirmation', { email }).subscribe({
      next: () => {
        this.resent.set(true);
        this.resending.set(false);
        this.startCooldown();
      },
      error: (err) => {
        this.error.set(err?.error?.message ?? 'CONFIRM_EMAIL.RESEND_ERROR');
        this.resending.set(false);
      },
    });
  }

  formatCooldown(seconds: number): string {
    const m = Math.floor(seconds / 60);
    const s = seconds % 60;
    return `${m}:${s.toString().padStart(2, '0')}`;
  }

  logout(): void {
    this.authService.logout();
    void this.router.navigateByUrl('/auth/login');
  }

  private startCooldown(): void {
    this.cooldownSeconds.set(RESEND_COOLDOWN_SECONDS);
    this.cooldownTimer = setInterval(() => {
      this.cooldownSeconds.update(s => {
        if (s <= 1) {
          this.clearTimer();
          return 0;
        }
        return s - 1;
      });
    }, 1000);
  }

  private clearTimer(): void {
    if (this.cooldownTimer !== null) {
      clearInterval(this.cooldownTimer);
      this.cooldownTimer = null;
    }
  }
}
