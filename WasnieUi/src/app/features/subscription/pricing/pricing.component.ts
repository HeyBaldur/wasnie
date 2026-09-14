import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { RouterLink } from '@angular/router';
import { TranslatePipe } from '@ngx-translate/core';
import { AppShellComponent } from '../../../shared/components/app-shell/app-shell.component';
import { IconComponent } from '../../../shared/components/icon/icon.component';
import { WsButtonComponent, WsCardComponent } from '../../../shared/ui';
import { WsToastService } from '../../../shared/ui/ws-toast/ws-toast.service';
import { HasPermissionPipe } from '../../../shared/pipes/has-permission.pipe';
import { AccountAccess, SubscriptionPlan, SubscriptionService } from '../services/subscription.service';
import { PlanOfferComponent } from '../plan-offer/plan-offer.component';
import { startCheckout } from '../plan-offer/start-checkout';

/**
 * Pricing (KAN-77): what the plan is, what it costs, and why it is worth it — for the trial user who decides to
 * pay. Billing for someone already paying lives in "Manage billing" (/billing); the two used to share one page.
 *
 * ★ ONE PACKAGE, PRESENTED AS ONE. A hero that says so, the featured plan card, then the reasons (benefits), the
 * reassurance (guarantees) and the answers (FAQ). Every claim on this page is something the product actually does:
 * the FAQ answers are written from the paywall, trial and billing-portal behaviour, not from marketing wishes.
 */
@Component({
  selector: 'app-pricing',
  standalone: true,
  imports: [
    DatePipe, RouterLink, TranslatePipe, AppShellComponent, IconComponent, WsCardComponent,
    WsButtonComponent, HasPermissionPipe, PlanOfferComponent,
  ],
  templateUrl: './pricing.component.html',
  styleUrl: './pricing.component.scss',
})
export class PricingComponent implements OnInit {
  private readonly subscriptionService = inject(SubscriptionService);
  private readonly toast = inject(WsToastService);

  readonly plans = signal<SubscriptionPlan[]>([]);
  readonly access = signal<AccountAccess | null>(null);
  readonly loading = signal(true);
  readonly loadError = signal(false);
  readonly subscribing = signal<string | null>(null);

  readonly isPaying = computed(() => this.access()?.state === 'Active');

  readonly benefits = [
    { icon: 'zap', title: 'PRICING.BENEFIT_CALC_TITLE', desc: 'PRICING.BENEFIT_CALC_DESC' },
    { icon: 'receipt', title: 'PRICING.BENEFIT_PAYRUNS_TITLE', desc: 'PRICING.BENEFIT_PAYRUNS_DESC' },
    { icon: 'assistant-chat', title: 'PRICING.BENEFIT_ASSISTANT_TITLE', desc: 'PRICING.BENEFIT_ASSISTANT_DESC' },
    { icon: 'refresh', title: 'PRICING.BENEFIT_HUBSPOT_TITLE', desc: 'PRICING.BENEFIT_HUBSPOT_DESC' },
    { icon: 'shield-check', title: 'PRICING.BENEFIT_AUDIT_TITLE', desc: 'PRICING.BENEFIT_AUDIT_DESC' },
    { icon: 'users', title: 'PRICING.BENEFIT_TEAM_TITLE', desc: 'PRICING.BENEFIT_TEAM_DESC' },
  ] as const;

  readonly guarantees = [
    { icon: 'calendar', key: 'PRICING.TRUST_TRIAL' },
    { icon: 'rotate-ccw', key: 'PRICING.TRUST_CANCEL' },
    { icon: 'lock', key: 'PRICING.TRUST_DATA' },
    { icon: 'shield', key: 'PRICING.TRUST_SECURE' },
  ] as const;

  readonly faqs = [
    { q: 'PRICING.FAQ_TRIAL_Q', a: 'PRICING.FAQ_TRIAL_A' },
    { q: 'PRICING.FAQ_CANCEL_Q', a: 'PRICING.FAQ_CANCEL_A' },
    { q: 'PRICING.FAQ_LIMITS_Q', a: 'PRICING.FAQ_LIMITS_A' },
    { q: 'PRICING.FAQ_PAYMENT_Q', a: 'PRICING.FAQ_PAYMENT_A' },
  ] as const;

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.loadError.set(false);
    this.subscriptionService.getAccess().subscribe({ next: a => this.access.set(a), error: () => this.access.set(null) });
    this.subscriptionService.getPlans().subscribe({
      next: plans => {
        this.plans.set(plans);
        this.loading.set(false);
      },
      error: () => {
        this.loading.set(false);
        this.loadError.set(true);
      },
    });
  }

  subscribe(plan: SubscriptionPlan): void {
    startCheckout(this.subscriptionService, this.toast, plan, this.subscribing);
  }
}
