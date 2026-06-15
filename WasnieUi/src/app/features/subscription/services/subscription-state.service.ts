import { Injectable, computed, inject, signal } from '@angular/core';
import { CurrentSubscription, SubscriptionService } from './subscription.service';

@Injectable({ providedIn: 'root' })
export class SubscriptionStateService {
  private readonly subscriptionService = inject(SubscriptionService);

  private readonly _subscription = signal<CurrentSubscription | null>(null);
  private readonly _loaded = signal(false);

  readonly subscription = this._subscription.asReadonly();
  readonly loaded = this._loaded.asReadonly();

  readonly isPastDue = computed(() => this._subscription()?.status === 'PastDue');
  readonly isCanceled = computed(() => this._subscription()?.status === 'Canceled');

  load(): void {
    this.subscriptionService.getCurrent().subscribe({
      next: sub => {
        this._subscription.set(sub);
        this._loaded.set(true);
      },
      error: () => {
        this._loaded.set(true);
      },
    });
  }

  refresh(): void {
    this._loaded.set(false);
    this.load();
  }
}
