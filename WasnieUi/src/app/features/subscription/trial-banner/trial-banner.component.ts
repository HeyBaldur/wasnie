import { Component, computed, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslatePipe } from '@ngx-translate/core';
import { WsButtonComponent } from '../../../shared/ui';
import { HasPermissionPipe } from '../../../shared/pipes/has-permission.pipe';
import { SubscriptionStateService } from '../services/subscription-state.service';

/** From this many days left the banner changes tone: the trial is about to end and the paywall is close. */
export const TRIAL_ENDING_SOON_DAYS = 2;

/**
 * KAN-77 — how many days of the free trial are left, visible on every screen, so the paywall never comes as a
 * surprise. Shell level, like the past-due banner. The days come from the server (rounded up), never counted
 * from the browser's clock.
 */
@Component({
  selector: 'app-trial-banner',
  standalone: true,
  imports: [RouterLink, TranslatePipe, WsButtonComponent, HasPermissionPipe],
  templateUrl: './trial-banner.component.html',
  styleUrl: './trial-banner.component.scss',
})
export class TrialBannerComponent {
  readonly subState = inject(SubscriptionStateService);

  readonly days = computed(() => this.subState.trialDaysRemaining() ?? 0);
  readonly endingSoon = computed(() => this.days() <= TRIAL_ENDING_SOON_DAYS);
  /** One day and several days are different sentences in every language — two literal keys, not a plural hack. */
  readonly messageKey = computed(() => (this.days() === 1 ? 'TRIAL_BANNER.ONE_DAY' : 'TRIAL_BANNER.DAYS'));
}
