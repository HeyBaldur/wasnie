import { Injectable, computed, inject, signal } from '@angular/core';
import { AccountAccess, CurrentSubscription, SubscriptionService } from './subscription.service';

/**
 * The account's billing state for the shell: the Stripe subscription (when there is one) and the ACCESS state
 * (KAN-77: trial / active / locked). Loaded once by the app shell and read by the banners and the sidebar.
 *
 * ★ A tenant in trial has NO subscription — `GET /subscription/current` answers 404 for it. That is not an
 * error to show; it is the normal state of every new account, so `subscription` simply stays null.
 */
@Injectable({ providedIn: 'root' })
export class SubscriptionStateService {
  private readonly subscriptionService = inject(SubscriptionService);

  private readonly _subscription = signal<CurrentSubscription | null>(null);
  private readonly _access = signal<AccountAccess | null>(null);
  private readonly _loaded = signal(false);

  readonly subscription = this._subscription.asReadonly();
  readonly access = this._access.asReadonly();
  readonly loaded = this._loaded.asReadonly();

  readonly isPastDue = computed(() => this._subscription()?.status === 'PastDue');
  readonly isTrial = computed(() => this._access()?.state === 'Trial');
  readonly isLocked = computed(() => this._access()?.state === 'Locked');
  readonly trialDaysRemaining = computed(() => this._access()?.trialDaysRemaining ?? null);

  load(): void {
    this.subscriptionService.getCurrent().subscribe({
      next: sub => this._subscription.set(sub),
      error: () => this._subscription.set(null),
    });
    this.subscriptionService.getAccess().subscribe({
      next: access => {
        this._access.set(access);
        this._loaded.set(true);
      },
      error: () => this._loaded.set(true),
    });
  }

  refresh(): void {
    this._loaded.set(false);
    this.load();
  }
}
