import { Component, input, output } from '@angular/core';
import { TranslatePipe } from '@ngx-translate/core';
import { WsBadgeComponent, WsButtonComponent } from '../../../shared/ui';
import { IconComponent } from '../../../shared/components/icon/icon.component';
import { CurrencyFormatPipe } from '../../../shared/pipes/currency-format.pipe';
import { SubscriptionPlan } from '../services/subscription.service';

/**
 * One plan, as it is offered: "this is what you get, this is what it costs" (KAN-77).
 *
 * ★ THE FEATURED CARD. One package, so it is presented the way a single flagship offer is: an inverted surface
 * (dark on the light theme, light on the dark theme — built from the text/inverse tokens, so it flips with the
 * theme instead of hard-coding a colour), price and action on one side, everything included on the other.
 *
 * ★ NO COMPARISON TABLE. The day there are several plans, each gets its own card; the limits come from the plan.
 * Shared by the Pricing page and the paywall, so the offer reads identically wherever someone decides to pay.
 */
@Component({
  selector: 'app-plan-offer',
  standalone: true,
  imports: [TranslatePipe, WsButtonComponent, WsBadgeComponent, IconComponent, CurrencyFormatPipe],
  templateUrl: './plan-offer.component.html',
  styleUrl: './plan-offer.component.scss',
})
export class PlanOfferComponent {
  readonly plan = input.required<SubscriptionPlan>();
  /** The account is paying for this plan right now. */
  readonly current = input(false);
  /** Hidden, not disabled (§5.8): someone without Subscription.Manage is not offered the button at all. */
  readonly canSubscribe = input(true);
  readonly busy = input(false);
  readonly subscribe = output<SubscriptionPlan>();

  readonly included = [
    'PRICING.INCLUDED_USERS',
    'PRICING.INCLUDED_ENGINE',
    'PRICING.INCLUDED_PAY_RUNS',
    'PRICING.INCLUDED_ASSISTANT',
    'PRICING.INCLUDED_HUBSPOT',
    'PRICING.INCLUDED_AUDIT',
    'PRICING.INCLUDED_EXCEL',
    'PRICING.INCLUDED_SUPPORT',
  ] as const;
}
