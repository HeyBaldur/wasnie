import { Component, inject, OnInit, signal } from '@angular/core';
import { Router } from '@angular/router';
import { TranslatePipe } from '@ngx-translate/core';
import { WsButtonComponent } from '../../ui';
import { ProfileService } from '../../../features/profile/services/profile.service';
import { AuthService } from '../../../core/services/auth.service';
import {
  TwoFaReminderPreference, UI_PREFERENCE_KEYS, UiPreferencesService, isTwoFaReminderSuppressed,
} from '../../../core/services/ui-preferences.service';

const SNOOZE_MS = 3 * 24 * 60 * 60 * 1000;
const APPEAR_DELAY_MS = 4000;

/**
 * "Protect your account" (2FA). ★ The three answers are remembered per USER on the server (KAN-78): "Remind me in 3
 * days" stores the date it may come back, "Don't show again" is permanent — on every machine, not just this browser.
 */
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
  private readonly preferences = inject(UiPreferencesService);

  readonly visible = signal(false);
  readonly hiding = signal(false);

  async ngOnInit(): Promise<void> {
    if (!this.auth.isAuthenticated()) return;
    // Unknown preferences show nothing: a reminder the user already declined must not return on a guess.
    if (!(await this.preferences.ensureLoaded())) return;
    if (isTwoFaReminderSuppressed(this.preferences.get(UI_PREFERENCE_KEYS.twoFaReminder))) return;

    this.profileService.getTwoFactorStatus().subscribe({
      next: (status) => {
        if (!status.isEnabled) {
          setTimeout(() => this.visible.set(true), APPEAR_DELAY_MS);
        }
      },
    });
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
    this.remember({ state: 'snoozed', until: new Date(Date.now() + SNOOZE_MS).toISOString() });
    this.animateOut(() => {});
  }

  dismiss(): void {
    this.remember({ state: 'dismissed' });
    this.animateOut(() => {});
  }

  private remember(choice: TwoFaReminderPreference): void {
    void this.preferences.set(UI_PREFERENCE_KEYS.twoFaReminder, JSON.stringify(choice));
  }
}
