import { HttpErrorResponse } from '@angular/common/http';
import { WritableSignal } from '@angular/core';
import { WsToastService } from '../../../shared/ui/ws-toast/ws-toast.service';
import { SubscriptionPlan, SubscriptionService } from '../services/subscription.service';

/** Where the browser goes to pay. A seam so tests can observe the redirect without leaving the page. */
export const checkoutNavigator = {
  go: (url: string): void => window.location.assign(url),
};

/**
 * Sends the user to Stripe Checkout for a plan — the one path to paying, shared by Pricing and the paywall so
 * both handle a refusal the same way.
 *
 * A 409 is a refusal: the tenant already has a live subscription (a second checkout would charge twice — KAN-77),
 * or the current usage does not fit the plan's limits (dormant while the only plan is unlimited). Any other failure
 * is Stripe being unreachable. None leaves the button spinning.
 */
export const ALREADY_SUBSCRIBED_REASON = 'AlreadySubscribed';

export function startCheckout(
  service: SubscriptionService,
  toast: WsToastService,
  plan: SubscriptionPlan,
  busy: WritableSignal<string | null>,
): void {
  if (!plan.priceId || busy()) return;

  busy.set(plan.planCode);
  service.createCheckout(plan.priceId).subscribe({
    next: ({ checkoutUrl }) => checkoutNavigator.go(checkoutUrl),
    error: (err: HttpErrorResponse) => {
      busy.set(null);
      if (err.status === 409) {
        const reason = (err.error as { blockedReason?: string } | null)?.blockedReason;
        toast.show(reason === ALREADY_SUBSCRIBED_REASON ? 'PRICING.CHECKOUT_ALREADY_SUBSCRIBED' : 'PRICING.CHECKOUT_BLOCKED', 'error');
        return;
      }
      toast.show('PRICING.CHECKOUT_ERROR', 'error');
    },
  });
}
