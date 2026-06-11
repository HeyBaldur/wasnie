import { Component, inject } from '@angular/core';
import { Router } from '@angular/router';
import { TranslatePipe } from '@ngx-translate/core';
import { WsButtonComponent, WsCardComponent } from '../../../shared/ui';

@Component({
  selector: 'app-subscription-reactivation',
  standalone: true,
  imports: [TranslatePipe, WsButtonComponent, WsCardComponent],
  templateUrl: './subscription-reactivation.component.html',
  styleUrl: './subscription-reactivation.component.scss',
})
export class SubscriptionReactivationComponent {
  private readonly router = inject(Router);

  reactivate(): void {
    void this.router.navigateByUrl('/onboarding/plan');
  }
}
