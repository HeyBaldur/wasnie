import { Component, inject, signal, OnInit, OnDestroy } from '@angular/core';
import { DatePipe } from '@angular/common';
import { TranslatePipe } from '@ngx-translate/core';
import { HttpErrorResponse } from '@angular/common/http';
import { Router } from '@angular/router';
import { AppShellComponent } from '../../../shared/components/app-shell/app-shell.component';
import { WsPageLayoutComponent, WsButtonComponent, WsBadgeComponent, WsCardComponent } from '../../../shared/ui';
import { WsToastService } from '../../../shared/ui/ws-toast/ws-toast.service';
import { SubscriptionService, CurrentSubscription, SubscriptionPlan } from '../services/subscription.service';

export interface BlockedInfo {
  reason: 'payees' | 'plans';
  tier: string;
  current: number;
  limit: number;
}

const POLL_INTERVAL_MS = 2_000;
const MAX_POLL_ATTEMPTS = 10;

@Component({
  selector: 'app-manage-subscription',
  standalone: true,
  imports: [DatePipe, TranslatePipe, AppShellComponent, WsPageLayoutComponent, WsButtonComponent, WsBadgeComponent, WsCardComponent],
  templateUrl: './manage-subscription.component.html',
  styleUrl: './manage-subscription.component.scss',
})
export class ManageSubscriptionComponent implements OnInit, OnDestroy {
  private readonly subscriptionService = inject(SubscriptionService);
  private readonly toast = inject(WsToastService);
  private readonly router = inject(Router);

  readonly subscription = signal<CurrentSubscription | null>(null);
  readonly plans = signal<SubscriptionPlan[]>([]);
  readonly loading = signal(true);
  readonly loadError = signal(false);
  readonly changingPlan = signal<string | null>(null);
  readonly blockedInfo = signal<BlockedInfo | null>(null);
  readonly openingPortal = signal(false);
  readonly pendingTier = signal<string | null>(null);
  readonly pollTimedOut = signal(false);
  readonly revertingCancellation = signal(false);

  private pollHandle: ReturnType<typeof setInterval> | null = null;
  private pollAttempts = 0;

  ngOnInit(): void {
    this.load();
  }

  ngOnDestroy(): void {
    this.stopPolling();
  }

  load(): void {
    this.loading.set(true);
    this.loadError.set(false);

    this.subscriptionService.getCurrent().subscribe({
      next: sub => {
        if (sub.status === 'Canceled') {
          void this.router.navigate(['/subscription/reactivate']);
          return;
        }
        this.subscription.set(sub);
        this.loadPlans();
      },
      error: () => {
        this.loading.set(false);
        this.loadError.set(true);
      },
    });
  }

  private loadPlans(): void {
    this.subscriptionService.getPlans().subscribe({
      next: plans => {
        this.plans.set(plans);
        this.loading.set(false);
      },
      error: () => {
        this.loading.set(false);
      },
    });
  }

  changePlan(targetTier: string): void {
    this.blockedInfo.set(null);
    this.pollTimedOut.set(false);
    this.changingPlan.set(targetTier);

    if (this.isFree) {
      const plan = this.plans().find(p => p.tier === targetTier);
      if (!plan?.priceId) { this.changingPlan.set(null); return; }
      this.subscriptionService.createCheckout(plan.priceId).subscribe({
        next: ({ checkoutUrl }) => window.location.assign(checkoutUrl),
        error: () => {
          this.changingPlan.set(null);
          this.toast.show('SUBSCRIPTION.CHANGE_ERROR', 'error');
        },
      });
      return;
    }

    const upgrading = this.isUpgrade(targetTier);
    this.subscriptionService.changePlan(targetTier).subscribe({
      next: () => {
        this.changingPlan.set(null);
        this.startPolling(targetTier);
      },
      error: (err: HttpErrorResponse) => {
        this.changingPlan.set(null);
        if (err.status === 409 && err.error?.blocked) {
          const body = err.error;
          this.blockedInfo.set({
            reason: body.blockedReason === 'payees' ? 'payees' : 'plans',
            tier: body.targetTier ?? targetTier,
            current: body.current ?? 0,
            limit: body.limit ?? 0,
          });
        } else if (upgrading && err.status === 400 && err.error?.message === 'upgrade_payment_failed') {
          this.toast.show('SUBSCRIPTION.UPGRADE_PAYMENT_FAILED', 'error');
        } else {
          this.toast.show('SUBSCRIPTION.CHANGE_ERROR', 'error');
        }
      },
    });
  }

  private startPolling(targetTier: string): void {
    this.pendingTier.set(targetTier);
    this.pollAttempts = 0;
    this.stopPolling();
    this.poll();
    this.pollHandle = setInterval(() => this.poll(), POLL_INTERVAL_MS);
  }

  private poll(): void {
    this.pollAttempts++;
    this.subscriptionService.getCurrent().subscribe({
      next: sub => {
        if (sub.tier === this.pendingTier()) {
          this.subscription.set(sub);
          this.pendingTier.set(null);
          this.stopPolling();
          this.loadPlans();
        } else if (this.pollAttempts >= MAX_POLL_ATTEMPTS) {
          this.pollTimedOut.set(true);
          this.pendingTier.set(null);
          this.stopPolling();
        }
      },
      error: () => {
        if (this.pollAttempts >= MAX_POLL_ATTEMPTS) {
          this.pollTimedOut.set(true);
          this.pendingTier.set(null);
          this.stopPolling();
        }
      },
    });
  }

  private stopPolling(): void {
    if (this.pollHandle !== null) {
      clearInterval(this.pollHandle);
      this.pollHandle = null;
    }
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

  get isCancelScheduled(): boolean {
    const sub = this.subscription();
    return sub?.status === 'Active' && sub?.cancelAtPeriodEnd === true;
  }

  get isFree(): boolean {
    return this.subscription()?.tier === 'Free';
  }

  get hasPaidSubscription(): boolean {
    const sub = this.subscription();
    return !!sub?.stripeSubscriptionId && sub.status === 'Active';
  }

  get statusVariant(): 'success' | 'warning' | 'danger' | 'neutral' {
    const status = this.subscription()?.status;
    if (status === 'Active') return 'success';
    if (status === 'PastDue') return 'warning';
    if (status === 'Canceled') return 'danger';
    return 'neutral';
  }

  maxPayeesDisplay(plan: SubscriptionPlan): string {
    return plan.maxPayees === -1 ? '∞' : String(plan.maxPayees);
  }

  maxPlansDisplay(plan: SubscriptionPlan): string {
    return plan.maxPlans === -1 ? '∞' : String(plan.maxPlans);
  }

  get upgradePlans(): SubscriptionPlan[] {
    return this.plans().filter(p => p.tier !== 'Free');
  }

  isUpgrade(planTier: string): boolean {
    const tiers = ['Free', 'Starter', 'Growth', 'Scale', 'Enterprise'];
    const current = tiers.indexOf(this.subscription()?.tier ?? 'Free');
    const target = tiers.indexOf(planTier);
    return target > current;
  }

  isChangingTo(tier: string): boolean {
    return this.changingPlan() === tier;
  }
}
