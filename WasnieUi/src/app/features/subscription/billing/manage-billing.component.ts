import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { TranslatePipe } from '@ngx-translate/core';
import { AppShellComponent } from '../../../shared/components/app-shell/app-shell.component';
import { IconComponent } from '../../../shared/components/icon/icon.component';
import { CurrencyFormatPipe } from '../../../shared/pipes/currency-format.pipe';
import {
  WsBadgeComponent, WsButtonComponent, WsCardComponent, WsEmptyStateComponent, WsPageLayoutComponent, WsTableComponent,
} from '../../../shared/ui';
import type { BadgeVariant } from '../../../shared/ui/ws-badge/ws-badge.component';
import { WsToastService } from '../../../shared/ui/ws-toast/ws-toast.service';
import { SubscriptionStateService } from '../services/subscription-state.service';
import {
  AccountAccess, BillingDetails, BillingInvoice, BoostOffer, CurrentSubscription, SubscriptionPlan,
  SubscriptionService, SubscriptionUsage,
} from '../services/subscription.service';
import {
  cardBrandLabel, invoiceStatusKey, invoiceStatusVariant, isLiveSubscriptionEnded, liveSubscriptionStatusKey,
  liveSubscriptionStatusVariant, resolveBillingPeriod, trialRemaining,
} from './billing-display';
import { TokenUsageMeterComponent } from '../token-usage/token-usage-meter.component';
import { checkoutNavigator } from '../plan-offer/start-checkout';
import { AddTokensDialogComponent } from '../add-tokens/add-tokens-dialog.component';
import { refreshAfterTokenPurchase } from '../add-tokens/token-purchase-return';

/**
 * Explicit translation keys per subscription status — never `'STATUS_' + status` (§C2): an unknown status from
 * Stripe must not print an internal identifier.
 */
export function subscriptionStatusKey(status: string | null | undefined): string {
  switch (status) {
    case 'Active': return 'SUBSCRIPTION.STATUS_ACTIVE';
    case 'PastDue': return 'SUBSCRIPTION.STATUS_PASTDUE';
    case 'Canceled': return 'SUBSCRIPTION.STATUS_CANCELED';
    case 'Incomplete': return 'SUBSCRIPTION.STATUS_INCOMPLETE';
    case 'Trialing': return 'SUBSCRIPTION.STATUS_TRIALING';
    default: return 'SUBSCRIPTION.STATUS_UNKNOWN';
  }
}

/** The ring's circumference for r = 26 (the SVG in the template). */
const RING_CIRCUMFERENCE = 2 * Math.PI * 26;

/**
 * Manage billing (KAN-77), in Settings: the subscription at a glance (plan, price, where the period stands), the
 * payment information and the invoice history — the last two read live from Stripe. Changing the card, cancelling
 * or paying an open invoice still happens in the Stripe billing portal; this page shows, the portal edits.
 *
 * ★ ONE CARD PER STATE (KAN-77, runtime). Trial: days left with a progress ring — the countdown only means something
 * there. Paying: plan, status, price and the renewal date, no ring. Ended: when it ended and the way back. A locked
 * account never reaches this page (the paywall does).
 *
 * ★ STRIPE'S LIVE SUBSCRIPTION WINS OVER THE STORED ROW for status and period: the row only moves when a webhook
 * arrives, and a missed one left "Active · Renews on Aug 24 · 0 days left" on screen in September.
 */
@Component({
  selector: 'app-manage-billing',
  standalone: true,
  imports: [
    DatePipe, RouterLink, TranslatePipe, CurrencyFormatPipe, AppShellComponent, IconComponent, WsPageLayoutComponent,
    WsCardComponent, WsButtonComponent, WsBadgeComponent, WsTableComponent, WsEmptyStateComponent,
    TokenUsageMeterComponent, AddTokensDialogComponent,
  ],
  templateUrl: './manage-billing.component.html',
  styleUrl: './manage-billing.component.scss',
})
export class ManageBillingComponent implements OnInit {
  private readonly subscriptionService = inject(SubscriptionService);
  private readonly toast = inject(WsToastService);
  private readonly subscriptionState = inject(SubscriptionStateService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);

  readonly access = signal<AccountAccess | null>(null);
  readonly subscription = signal<CurrentSubscription | null>(null);
  readonly plans = signal<SubscriptionPlan[]>([]);
  readonly usage = signal<SubscriptionUsage | null>(null);
  readonly details = signal<BillingDetails | null>(null);
  readonly detailsLoading = signal(true);
  readonly detailsError = signal(false);
  readonly loading = signal(true);
  readonly loadError = signal(false);
  readonly openingPortal = signal(false);
  readonly revertingCancellation = signal(false);

  // KAN-83 — whether token add-ons can be bought at all, and the purchase dialog's open state.
  readonly boostOffers = signal<BoostOffer[]>([]);
  readonly addTokensOpen = signal(false);

  readonly ringCircumference = RING_CIRCUMFERENCE;

  /**
   * KAN-83 — add-ons are offered only to an account that actually pays, and only once there is something to offer.
   * A trial's way forward is to subscribe, not to top up an account that is about to lock (§C3).
   */
  readonly canBuyTokens = computed(() => this.cardState() === 'paid' && this.boostOffers().length > 0);

  /** A real Stripe subscription — the only thing the billing portal and the period apply to. */
  readonly hasStripeSubscription = computed(() => !!this.subscription()?.stripeSubscriptionId);
  readonly hasStripeCustomer = computed(() => !!this.subscription()?.stripeCustomerId);
  readonly isTrial = computed(() => this.access()?.state === 'Trial');
  readonly isPastDue = computed(() => {
    const live = this.details()?.subscription;
    return live ? live.status === 'past_due' || live.status === 'unpaid' : this.subscription()?.status === 'PastDue';
  });
  readonly liveSubscription = computed(() => this.details()?.subscription ?? null);

  /** Stripe says the subscription is over, whatever the stored row still says. */
  readonly isEnded = computed(() => {
    const live = this.liveSubscription();
    return live ? isLiveSubscriptionEnded(live.status) : this.subscription()?.status === 'Canceled';
  });

  readonly isCancelScheduled = computed(() => {
    if (this.isEnded()) return false;
    // Classic (cancel_at_period_end) and flexible billing (cancel_at) both schedule the end — see the webhook.
    const live = this.liveSubscription();
    if (live) return live.status === 'active' && (live.cancelAtPeriodEnd || !!live.cancelAt);
    const sub = this.subscription();
    return !!sub && sub.status === 'Active' && (sub.cancelAtPeriodEnd || !!sub.cancelAt);
  });

  /** Which card to draw. Trial wins only while there is no real Stripe subscription behind the account. */
  readonly cardState = computed<'trial' | 'paid' | 'ended' | 'none'>(() => {
    if (this.isTrial() && !this.hasStripeSubscription()) return 'trial';
    if (!this.hasStripeSubscription()) return 'none';
    return this.isEnded() ? 'ended' : 'paid';
  });

  readonly currentPlan = computed(() => {
    const code = this.subscription()?.planCode;
    const plans = this.plans();
    return plans.find(p => p.planCode === code) ?? (this.isTrial() ? plans[0] : undefined) ?? null;
  });

  /** The plan's display name from Stripe when it is still offered; the code otherwise. */
  readonly planName = computed(() => {
    const code = this.subscription()?.planCode;
    return this.plans().find(p => p.planCode === code)?.name ?? code ?? '';
  });

  readonly statusKey = computed(() => {
    const live = this.liveSubscription();
    return live ? liveSubscriptionStatusKey(live.status) : subscriptionStatusKey(this.subscription()?.status);
  });
  readonly statusVariant = computed<BadgeVariant>(() => {
    const live = this.liveSubscription();
    if (live) return liveSubscriptionStatusVariant(live.status);
    switch (this.subscription()?.status) {
      case 'Active': return 'success';
      case 'PastDue': return 'warning';
      case 'Canceled':
      case 'Incomplete': return 'danger';
      default: return 'neutral';
    }
  });

  /** Waits for Stripe before falling back to the stored period, so the stale date never flashes first. */
  readonly period = computed(() =>
    this.detailsLoading() ? null : resolveBillingPeriod(this.liveSubscription(), this.subscription()));

  /** The date the subscription ends or ended, for the scheduled-cancellation and ended states. */
  readonly endDate = computed(() => {
    const live = this.liveSubscription();
    const sub = this.subscription();
    return live?.endedAt ?? live?.cancelAt ?? sub?.canceledAt ?? sub?.cancelAt ?? this.period()?.end ?? null;
  });

  /** Trial ring: share of the trial still ahead. Null draws no ring. */
  readonly ringValue = computed(() => {
    const a = this.access();
    return a ? trialRemaining(a.trialDaysRemaining, a.trialLengthDays) : null;
  });
  readonly ringOffset = computed(() => RING_CIRCUMFERENCE * (1 - (this.ringValue() ?? 0)));

  readonly paymentMethod = computed(() => this.details()?.paymentMethod ?? null);
  readonly cardBrand = computed(() => cardBrandLabel(this.paymentMethod()?.brand));
  readonly invoices = computed(() => this.details()?.invoices ?? []);

  readonly invoiceStatusKey = invoiceStatusKey;
  readonly invoiceStatusVariant = invoiceStatusVariant;

  ngOnInit(): void {
    this.load();
    // KAN-83 UX: back from Stripe after buying tokens — the credit arrives by webhook, a beat after the redirect.
    refreshAfterTokenPurchase(this.route.snapshot.queryParams, this.subscriptionState);
  }

  load(): void {
    this.loading.set(true);
    this.loadError.set(false);

    this.subscriptionService.getPlans().subscribe({ next: p => this.plans.set(p), error: () => this.plans.set([]) });
    // KAN-83: boosts may not be configured, and Stripe may be down. Either way the section simply does not appear —
    // the rest of the page is unaffected.
    this.subscriptionService.getBoostOffers().subscribe({
      next: o => this.boostOffers.set(o),
      error: () => this.boostOffers.set([]),
    });
    this.subscriptionService.getUsage().subscribe({ next: u => this.usage.set(u), error: () => this.usage.set(null) });
    // A trial has no subscription: 404 here is the normal state of a new account, not an error.
    this.subscriptionService.getCurrent().subscribe({ next: s => this.subscription.set(s), error: () => this.subscription.set(null) });
    this.subscriptionService.getAccess().subscribe({
      next: access => {
        this.access.set(access);
        this.loading.set(false);
      },
      error: () => {
        this.loading.set(false);
        this.loadError.set(true);
      },
    });
    this.loadDetails();
  }

  /**
   * What an unpaid invoice should say about its retries, or null when there is nothing to say.
   *
   * ★ ONLY FOR AN INVOICE THAT IS STILL OPEN AND HAS ACTUALLY BEEN TRIED. A paid invoice's attempt count is
   * history nobody needs, and "0 attempts" is noise. Returning the KEY and its parameters keeps the choice of
   * sentence here and the wording in the translations (§C1).
   */
  invoiceRetryNote(invoice: BillingInvoice): { key: string; params: Record<string, unknown> } | null {
    if (invoice.status !== 'open' || invoice.attemptCount < 1) {
      return null;
    }

    return invoice.nextPaymentAttempt
      ? { key: 'BILLING.INVOICE_RETRIED_NEXT', params: { count: invoice.attemptCount, date: invoice.nextPaymentAttempt } }
      : { key: 'BILLING.INVOICE_RETRIED', params: { count: invoice.attemptCount } };
  }

  /** KAN-83 — opens the shared purchase dialog. The money question is asked inside it, never here. */
  openAddTokens(): void {
    this.addTokensOpen.set(true);
  }

  /** Stripe can be slow or down; that only empties the two Stripe sections, never the whole page. */
  loadDetails(): void {
    this.detailsLoading.set(true);
    this.detailsError.set(false);
    this.subscriptionService.getBillingDetails().subscribe({
      next: d => {
        this.details.set(d);
        this.detailsLoading.set(false);
        if (d.synced) this.afterSync();
      },
      error: () => {
        this.details.set(null);
        this.detailsLoading.set(false);
        this.detailsError.set(true);
      },
    });
  }

  /**
   * ★ THE SERVER JUST CORRECTED THE SUBSCRIPTION FROM STRIPE. Everything that read the old row is stale: the app-wide
   * state (banners, guards), this page's stored subscription and the access state. A missed cancellation now means
   * a locked account, and a locked account belongs on the paywall — the same place the guards would send it.
   */
  private afterSync(): void {
    this.subscriptionState.refresh();
    this.subscriptionService.getCurrent().subscribe({ next: s => this.subscription.set(s), error: () => {} });
    this.subscriptionService.getAccess().subscribe({
      next: access => {
        this.access.set(access);
        if (access.state === 'Locked') this.router.navigate(['/billing/paywall']);
      },
      error: () => {},
    });
  }

  openBillingPortal(): void {
    this.openingPortal.set(true);
    this.subscriptionService.getBillingPortalUrl().subscribe({
      next: ({ url }) => {
        this.openingPortal.set(false);
        window.open(url, '_blank');
      },
      error: () => {
        this.openingPortal.set(false);
        this.toast.show('SUBSCRIPTION.BILLING_PORTAL_ERROR', 'error');
      },
    });
  }

  /** Invoice links come from Stripe (hosted page / PDF); opened without handing this window to them. */
  openInvoice(url: string | null): void {
    if (url) window.open(url, '_blank', 'noopener');
  }

  revertCancellation(): void {
    this.revertingCancellation.set(true);
    this.subscriptionService.revertCancellation().subscribe({
      next: () => {
        this.revertingCancellation.set(false);
        this.load();
      },
      error: () => {
        this.revertingCancellation.set(false);
        this.toast.show('SUBSCRIPTION.REVERT_CANCEL_ERROR', 'error');
      },
    });
  }
}
