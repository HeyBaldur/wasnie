import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { TranslatePipe } from '@ngx-translate/core';
import { WsButtonComponent, WsCardComponent } from '../../../shared/ui';
import { WsToastService } from '../../../shared/ui/ws-toast/ws-toast.service';
import { IconComponent } from '../../../shared/components/icon/icon.component';
import { HasPermissionPipe } from '../../../shared/pipes/has-permission.pipe';
import { AuthService } from '../../../core/services/auth.service';
import { AccountAccess, SubscriptionPlan, SubscriptionService } from '../services/subscription.service';
import { PlanOfferComponent } from '../plan-offer/plan-offer.component';
import { startCheckout } from '../plan-offer/start-checkout';

/**
 * The paywall (KAN-77): the trial ended without a subscription, or the subscription ended. Full screen, no app
 * shell — the shell's own requests would all answer 402 for a locked account.
 *
 * ★ BLOCKED, NOT DELETED. The copy says so, because it is the first thing someone fears on this screen, and it is
 * true: the moment they pay, the next request goes through with every plan, payee and credit where it was.
 */
@Component({
  selector: 'app-paywall',
  standalone: true,
  imports: [TranslatePipe, WsCardComponent, WsButtonComponent, HasPermissionPipe, PlanOfferComponent, IconComponent],
  templateUrl: './paywall.component.html',
  styleUrl: './paywall.component.scss',
})
export class PaywallComponent implements OnInit {
  private readonly subscriptionService = inject(SubscriptionService);
  private readonly authService = inject(AuthService);
  private readonly router = inject(Router);
  private readonly toast = inject(WsToastService);

  readonly access = signal<AccountAccess | null>(null);
  readonly plans = signal<SubscriptionPlan[]>([]);
  readonly loading = signal(true);
  readonly loadError = signal(false);
  readonly subscribing = signal<string | null>(null);

  /** Explicit keys per reason — never built from the reason string (§C2). */
  readonly titleKey = computed(() => {
    switch (this.access()?.lockReason) {
      case 'SubscriptionEnded': return 'PAYWALL.TITLE_SUBSCRIPTION_ENDED';
      case 'TrialEnded': return 'PAYWALL.TITLE_TRIAL_ENDED';
      default: return 'PAYWALL.TITLE_NO_SUBSCRIPTION';
    }
  });

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.loadError.set(false);
    this.subscriptionService.getAccess().subscribe({
      next: access => {
        // Paid in the meantime (or landed here by an old link): the paywall has nothing to say.
        if (access.state !== 'Locked') {
          void this.router.navigateByUrl('/dashboard');
          return;
        }
        this.access.set(access);
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
        this.loadError.set(true);
      },
    });
  }

  subscribe(plan: SubscriptionPlan): void {
    startCheckout(this.subscriptionService, this.toast, plan, this.subscribing);
  }

  signOut(): void {
    this.authService.logout();
    void this.router.navigateByUrl('/auth/login');
  }
}
