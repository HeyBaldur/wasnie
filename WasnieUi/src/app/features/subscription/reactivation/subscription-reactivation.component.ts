import { Component, inject, signal, OnInit } from '@angular/core';
import { DatePipe, DecimalPipe, UpperCasePipe } from '@angular/common';
import { Router } from '@angular/router';
import { TranslatePipe } from '@ngx-translate/core';
import { forkJoin } from 'rxjs';
import { WsButtonComponent, WsBadgeComponent, WsCardComponent } from '../../../shared/ui';
import { WsToastService } from '../../../shared/ui/ws-toast/ws-toast.service';
import { AuthService } from '../../../core/services/auth.service';
import { SubscriptionService, CurrentSubscription, SubscriptionPlan, SubscriptionUsage } from '../services/subscription.service';

@Component({
  selector: 'app-subscription-reactivation',
  standalone: true,
  imports: [DatePipe, DecimalPipe, UpperCasePipe, TranslatePipe, WsButtonComponent, WsBadgeComponent, WsCardComponent],
  templateUrl: './subscription-reactivation.component.html',
  styleUrl: './subscription-reactivation.component.scss',
})
export class SubscriptionReactivationComponent implements OnInit {
  private readonly subscriptionService = inject(SubscriptionService);
  private readonly authService = inject(AuthService);
  private readonly router = inject(Router);
  private readonly toast = inject(WsToastService);

  readonly subscription = signal<CurrentSubscription | null>(null);
  readonly plans = signal<SubscriptionPlan[]>([]);
  readonly usage = signal<SubscriptionUsage | null>(null);
  readonly loading = signal(true);
  readonly loadError = signal(false);
  readonly startingCheckout = signal<string | null>(null);

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.loadError.set(false);

    this.subscriptionService.getCurrent().subscribe({
      next: sub => {
        this.subscription.set(sub);
        forkJoin({
          plans: this.subscriptionService.getPlans(),
          usage: this.subscriptionService.getUsage(),
        }).subscribe({
          next: ({ plans, usage }) => {
            this.plans.set(plans.filter(p => p.tier !== 'Free' && p.priceId !== null));
            this.usage.set(usage);
            this.loading.set(false);
          },
          error: () => {
            this.loading.set(false);
          },
        });
      },
      error: () => {
        this.loading.set(false);
        this.loadError.set(true);
      },
    });
  }

  signOut(): void {
    this.authService.logout();
    void this.router.navigateByUrl('/auth/login');
  }

  reactivate(priceId: string, tier: string): void {
    this.startingCheckout.set(tier);
    this.subscriptionService.createCheckout(priceId).subscribe({
      next: ({ checkoutUrl }) => {
        window.location.assign(checkoutUrl);
      },
      error: () => {
        this.startingCheckout.set(null);
        this.toast.show('SUBSCRIPTION.REACTIVATION_CHECKOUT_ERROR', 'error');
      },
    });
  }

  isPreviousPlan(plan: SubscriptionPlan): boolean {
    return plan.tier === this.subscription()?.tier;
  }

  isStartingCheckoutFor(tier: string): boolean {
    return this.startingCheckout() === tier;
  }

  maxPayeesDisplay(plan: SubscriptionPlan): string {
    return plan.maxPayees === -1 ? '∞' : String(plan.maxPayees);
  }

  maxPlansDisplay(plan: SubscriptionPlan): string {
    return plan.maxPlans === -1 ? '∞' : String(plan.maxPlans);
  }

  isPlanBlockedByPayees(plan: SubscriptionPlan): boolean {
    const u = this.usage();
    return !!u && plan.maxPayees !== -1 && u.payeeCount > plan.maxPayees;
  }

  isPlanBlockedByPlans(plan: SubscriptionPlan): boolean {
    const u = this.usage();
    return !!u && plan.maxPlans !== -1 && u.planCount > plan.maxPlans;
  }

  isPlanBlocked(plan: SubscriptionPlan): boolean {
    return this.isPlanBlockedByPayees(plan) || this.isPlanBlockedByPlans(plan);
  }
}
