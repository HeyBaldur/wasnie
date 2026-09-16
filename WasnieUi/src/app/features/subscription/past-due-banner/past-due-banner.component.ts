import { Component, computed, effect, inject, signal, untracked } from '@angular/core';
import { TranslatePipe } from '@ngx-translate/core';
import { IconComponent } from '../../../shared/components/icon/icon.component';
import { DateFormatPipe } from '../../../shared/pipes/date-format.pipe';
import { WsButtonComponent } from '../../../shared/ui';
import { SubscriptionStateService } from '../services/subscription-state.service';
import { BillingInvoice, SubscriptionService } from '../services/subscription.service';
import { WsToastService } from '../../../shared/ui/ws-toast/ws-toast.service';

/**
 * The payment-overdue banner.
 *
 * ★★ IT NO LONGER PROMISES A "GRACE PERIOD". The old copy said access would be blocked "once the grace period
 * ends" — and no such period exists anywhere in this system. `AccountAccessPolicy` keeps a PastDue account in
 * full access until Stripe gives up retrying and CANCELS the subscription; nothing here defines a window,
 * measures one, or could tell the customer when it closes. So the banner named a deadline that could never be
 * shown, which is how it read as vague and slightly threatening at the same time (§C3).
 *
 * ★ WHAT IT SAYS INSTEAD IS A FACT WE HAVE: the date Stripe will try the card again, straight off the unpaid
 * invoice. When Stripe has not scheduled one, the banner says the charge failed and asks for a new card —
 * without inventing a deadline to go with it.
 */
@Component({
  selector: 'app-past-due-banner',
  standalone: true,
  imports: [TranslatePipe, DateFormatPipe, IconComponent, WsButtonComponent],
  templateUrl: './past-due-banner.component.html',
  styleUrl: './past-due-banner.component.scss',
})
export class PastDueBannerComponent {
  readonly subState = inject(SubscriptionStateService);
  private readonly subscriptionService = inject(SubscriptionService);
  private readonly toast = inject(WsToastService);

  readonly opening = signal(false);

  private readonly invoices = signal<BillingInvoice[]>([]);
  private loadedFor = false;

  constructor() {
    // ★ ONLY WHEN THE BANNER IS ACTUALLY UP. Billing details are a live Stripe read and this component sits
    // on every page; fetching them always would put a third-party call on the critical path of the whole app
    // for a state most accounts are never in. The flag keeps it to one call per PastDue spell.
    effect(() => {
      if (!this.subState.isPastDue() || this.loadedFor) {
        return;
      }

      this.loadedFor = true;
      untracked(() => this.subscriptionService.getBillingDetails().subscribe({
        next: d => this.invoices.set(d.invoices ?? []),
        // Stripe being unreachable must not take the banner down with it: the warning is still true, it just
        // loses the date.
        error: () => this.invoices.set([]),
      }));
    });
  }

  /** When Stripe will try the card again, straight off the unpaid invoice. Null when it has not scheduled one. */
  readonly retryDate = computed(() =>
    this.invoices().find(i => i.status === 'open' && i.nextPaymentAttempt)?.nextPaymentAttempt ?? null);

  openBillingPortal(): void {
    if (this.opening()) return;

    this.opening.set(true);
    this.subscriptionService.getBillingPortalUrl().subscribe({
      next: ({ url }) => {
        this.opening.set(false);
        window.open(url, '_blank');
      },
      error: () => {
        this.opening.set(false);
        this.toast.show('SUBSCRIPTION.BILLING_PORTAL_ERROR', 'error');
      },
    });
  }
}
