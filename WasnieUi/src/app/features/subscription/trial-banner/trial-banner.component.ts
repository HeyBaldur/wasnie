import { Component, computed, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslatePipe } from '@ngx-translate/core';
import { WsTooltipDirective } from '../../../shared/ui';
import { IconComponent } from '../../../shared/components/icon/icon.component';
import { DateFormatPipe } from '../../../shared/pipes/date-format.pipe';
import { HasPermissionPipe } from '../../../shared/pipes/has-permission.pipe';
import { SubscriptionStateService } from '../services/subscription-state.service';

/** From this many days left the banner changes tone: the trial is about to end and the paywall is close. */
export const TRIAL_ENDING_SOON_DAYS = 2;

/**
 * KAN-77 — how many days of the free trial are left, visible on every screen, so the paywall never comes as a
 * surprise. Lives in the TOPBAR (it was a full-width strip under it); the past-due banner stays a strip because it is
 * an alert. The days come from the server (rounded up), never counted
 * from the browser's clock.
 */
@Component({
  selector: 'app-trial-banner',
  standalone: true,
  imports: [RouterLink, TranslatePipe, WsTooltipDirective, IconComponent, DateFormatPipe, HasPermissionPipe],
  templateUrl: './trial-banner.component.html',
  styleUrl: './trial-banner.component.scss',
})
export class TrialBannerComponent {
  readonly subState = inject(SubscriptionStateService);

  readonly days = computed(() => this.subState.trialDaysRemaining() ?? 0);
  readonly endingSoon = computed(() => this.days() <= TRIAL_ENDING_SOON_DAYS);
  /** The exact end, from the server — shown on hover, since the day count is rounded up. */
  readonly endsAt = computed(() => this.subState.access()?.trialEndsAt ?? null);
  /** One day and several days are different sentences in every language — two literal keys, not a plural hack. */
  readonly messageKey = computed(() => (this.days() === 1 ? 'TRIAL_BANNER.ONE_DAY' : 'TRIAL_BANNER.DAYS'));
}
