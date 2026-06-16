import { Component, inject, OnInit, signal } from '@angular/core';
import { Router } from '@angular/router';
import { TranslatePipe } from '@ngx-translate/core';
import { WsButtonComponent } from '../../ui';
import { ProfileService } from '../../../features/profile/services/profile.service';
import { AuthService } from '../../../core/services/auth.service';

const SNOOZE_KEY = 'wasnie:2fa-reminder-snooze';
const DISMISS_KEY = 'wasnie:2fa-reminder-dismissed';
const SNOOZE_MS = 3 * 24 * 60 * 60 * 1000;
const APPEAR_DELAY_MS = 4000;

@Component({
  selector: 'app-two-fa-reminder',
  standalone: true,
  imports: [TranslatePipe, WsButtonComponent],
  templateUrl: './two-fa-reminder.component.html',
  styleUrl: './two-fa-reminder.component.scss',
})
export class TwoFaReminderComponent implements OnInit {
  private readonly profileService = inject(ProfileService);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  readonly visible = signal(false);
  readonly hiding = signal(false);

  ngOnInit(): void {
    if (!this.auth.isAuthenticated()) return;
    if (!this.shouldShow()) return;

    this.profileService.getTwoFactorStatus().subscribe({
      next: (status) => {
        if (!status.isEnabled) {
          setTimeout(() => this.visible.set(true), APPEAR_DELAY_MS);
        }
      },
    });
  }

  private shouldShow(): boolean {
    if (localStorage.getItem(DISMISS_KEY) === 'true') return false;

    const snoozeTs = localStorage.getItem(SNOOZE_KEY);
    if (snoozeTs) {
      const snoozedAt = parseInt(snoozeTs, 10);
      if (Date.now() < snoozedAt + SNOOZE_MS) return false;
    }

    return true;
  }

  private animateOut(then: () => void): void {
    this.hiding.set(true);
    setTimeout(() => {
      this.visible.set(false);
      this.hiding.set(false);
      then();
    }, 260);
  }

  close(): void {
    this.animateOut(() => {});
  }

  activate(): void {
    this.visible.set(false);
    this.router.navigate(['/profile'], { fragment: 'security' });
  }

  snooze(): void {
    localStorage.setItem(SNOOZE_KEY, Date.now().toString());
    this.animateOut(() => {});
  }

  dismiss(): void {
    localStorage.setItem(DISMISS_KEY, 'true');
    this.animateOut(() => {});
  }
}
