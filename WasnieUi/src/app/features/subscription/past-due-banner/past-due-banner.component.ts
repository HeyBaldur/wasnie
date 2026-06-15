import { Component, inject } from '@angular/core';
import { TranslatePipe } from '@ngx-translate/core';
import { WsButtonComponent } from '../../../shared/ui';
import { SubscriptionStateService } from '../services/subscription-state.service';
import { SubscriptionService } from '../services/subscription.service';
import { WsToastService } from '../../../shared/ui/ws-toast/ws-toast.service';

@Component({
  selector: 'app-past-due-banner',
  standalone: true,
  imports: [TranslatePipe, WsButtonComponent],
  templateUrl: './past-due-banner.component.html',
  styleUrl: './past-due-banner.component.scss',
})
export class PastDueBannerComponent {
  readonly subState = inject(SubscriptionStateService);
  private readonly subscriptionService = inject(SubscriptionService);
  private readonly toast = inject(WsToastService);

  openBillingPortal(): void {
    this.subscriptionService.getBillingPortalUrl().subscribe({
      next: ({ url }) => window.open(url, '_blank'),
      error: () => this.toast.show('SUBSCRIPTION.BILLING_PORTAL_ERROR', 'error'),
    });
  }
}
