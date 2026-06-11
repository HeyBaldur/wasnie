import { Component, inject, signal, OnInit } from '@angular/core';
import { DatePipe } from '@angular/common';
import { TranslatePipe } from '@ngx-translate/core';
import { AppShellComponent } from '../../../shared/components/app-shell/app-shell.component';
import { WsPageHeaderComponent, WsButtonComponent, WsBadgeComponent, WsCardComponent } from '../../../shared/ui';
import { CurrentUserService } from '../../../core/auth/current-user.service';
import { SubscriptionService, CurrentSubscription, SubscriptionPlan } from '../services/subscription.service';

@Component({
  selector: 'app-manage-subscription',
  standalone: true,
  imports: [DatePipe, TranslatePipe, AppShellComponent, WsPageHeaderComponent, WsButtonComponent, WsBadgeComponent, WsCardComponent],
  templateUrl: './manage-subscription.component.html',
  styleUrl: './manage-subscription.component.scss',
})
export class ManageSubscriptionComponent implements OnInit {
  private readonly subscriptionService = inject(SubscriptionService);
  readonly currentUser = inject(CurrentUserService);

  readonly subscription = signal<CurrentSubscription | null>(null);
  readonly plans = signal<SubscriptionPlan[]>([]);
  readonly loading = signal(true);
  readonly loadError = signal(false);

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.loadError.set(false);

    this.subscriptionService.getCurrent().subscribe({
      next: sub => {
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

  get isFree(): boolean {
    return this.subscription()?.tier === 'Free';
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
}
