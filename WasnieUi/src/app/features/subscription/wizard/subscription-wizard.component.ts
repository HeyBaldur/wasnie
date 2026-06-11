import { Component, inject, signal, computed, OnInit } from '@angular/core';
import { DOCUMENT } from '@angular/common';
import { Router } from '@angular/router';
import { TranslatePipe } from '@ngx-translate/core';
import { WsButtonComponent, WsBadgeComponent } from '../../../shared/ui';
import { CurrentUserService } from '../../../core/auth/current-user.service';
import { AuthService } from '../../../core/services/auth.service';
import { SubscriptionService, SubscriptionPlan } from '../services/subscription.service';

@Component({
  selector: 'app-subscription-wizard',
  standalone: true,
  imports: [TranslatePipe, WsButtonComponent, WsBadgeComponent],
  templateUrl: './subscription-wizard.component.html',
  styleUrl: './subscription-wizard.component.scss',
})
export class SubscriptionWizardComponent implements OnInit {
  private readonly subscriptionService = inject(SubscriptionService);
  private readonly currentUser = inject(CurrentUserService);
  private readonly authService = inject(AuthService);
  private readonly router = inject(Router);
  private readonly document = inject(DOCUMENT);

  readonly plans = signal<SubscriptionPlan[]>([]);
  readonly loading = signal(true);
  readonly loadError = signal(false);
  readonly submitting = signal(false);
  readonly checkoutError = signal(false);

  readonly userName = computed(() => {
    const u = this.currentUser.currentUser();
    return u?.email?.split('@')[0] ?? '';
  });

  ngOnInit(): void {
    this.loadPlans();
  }

  loadPlans(): void {
    this.loading.set(true);
    this.loadError.set(false);
    this.subscriptionService.getPlans().subscribe({
      next: plans => {
        this.plans.set(plans);
        this.loading.set(false);
      },
      error: () => {
        this.loadError.set(true);
        this.loading.set(false);
      },
    });
  }

  isPaid(plan: SubscriptionPlan): boolean {
    return plan.tier !== 'Free';
  }

  maxPayeesDisplay(plan: SubscriptionPlan): string {
    return plan.maxPayees === -1 ? '∞' : String(plan.maxPayees);
  }

  maxPlansDisplay(plan: SubscriptionPlan): string {
    return plan.maxPlans === -1 ? '∞' : String(plan.maxPlans);
  }

  signOut(): void {
    this.authService.logout();
    void this.router.navigateByUrl('/auth/login');
  }

  selectFree(): void {
    if (this.submitting()) return;
    this.submitting.set(true);
    this.subscriptionService.selectFree().subscribe({
      next: () => {
        this.currentUser.refresh().subscribe(() => {
          void this.router.navigateByUrl('/dashboard');
        });
      },
      error: () => {
        this.submitting.set(false);
      },
    });
  }

  protected redirect(url: string): void {
    this.document.defaultView!.location.href = url;
  }

  selectPaid(plan: SubscriptionPlan): void {
    if (this.submitting() || !plan.priceId) return;
    this.submitting.set(true);
    this.checkoutError.set(false);
    this.subscriptionService.createCheckout(plan.priceId).subscribe({
      next: ({ checkoutUrl }) => {
        this.redirect(checkoutUrl);
      },
      error: () => {
        this.submitting.set(false);
        this.checkoutError.set(true);
      },
    });
  }
}
