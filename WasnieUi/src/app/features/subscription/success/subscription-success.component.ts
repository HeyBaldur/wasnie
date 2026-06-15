import { Component, inject, signal, OnInit, OnDestroy } from '@angular/core';
import { Router } from '@angular/router';
import { TranslatePipe } from '@ngx-translate/core';
import { WsButtonComponent } from '../../../shared/ui';
import { SubscriptionService } from '../services/subscription.service';

const POLL_INTERVAL_MS = 2_000;
const MAX_POLL_ATTEMPTS = 15; // 30 seconds before timeout message

@Component({
  selector: 'app-subscription-success',
  standalone: true,
  imports: [TranslatePipe, WsButtonComponent],
  templateUrl: './subscription-success.component.html',
  styleUrl: './subscription-success.component.scss',
})
export class SubscriptionSuccessComponent implements OnInit, OnDestroy {
  private readonly subscriptionService = inject(SubscriptionService);
  private readonly router = inject(Router);

  readonly confirmed = signal(false);
  readonly timedOut = signal(false);

  private pollHandle: ReturnType<typeof setInterval> | null = null;
  private attempts = 0;

  ngOnInit(): void {
    this.pollHandle = setInterval(() => this.poll(), POLL_INTERVAL_MS);
    this.poll(); // first check immediately
  }

  ngOnDestroy(): void {
    this.stopPolling();
  }

  enterApp(): void {
    this.stopPolling();
    void this.router.navigateByUrl('/dashboard');
  }

  backToWizard(): void {
    this.stopPolling();
    void this.router.navigateByUrl('/onboarding/plan');
  }

  private poll(): void {
    this.attempts++;
    this.subscriptionService.getCurrent().subscribe({
      next: sub => {
        if (sub.status === 'Active' && sub.tier !== 'Free') {
          this.confirmed.set(true);
          this.stopPolling();
          setTimeout(() => this.enterApp(), 1_500);
        } else if (this.attempts >= MAX_POLL_ATTEMPTS) {
          this.timedOut.set(true);
          this.stopPolling();
        }
      },
      error: () => {
        if (this.attempts >= MAX_POLL_ATTEMPTS) {
          this.timedOut.set(true);
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
}
