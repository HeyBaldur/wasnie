import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../../environments/environment';

export interface SubscriptionPlan {
  priceId: string | null;
  productId: string | null;
  name: string;
  price: number;
  currency: string;
  interval: string;
  tier: string;
  maxPayees: number;
  maxPlans: number;
  isCurrentPlan: boolean;
}

export interface CurrentSubscription {
  tier: string;
  status: string;
  billingEmail: string;
  stripeSubscriptionId: string | null;
  stripeCustomerId: string | null;
  stripePriceId: string | null;
  stripeProductId: string | null;
  currentPeriodStart: string | null;
  currentPeriodEnd: string | null;
  nextBillingDate: string | null;
  canceledAt: string | null;
  createdAt: string;
}

@Injectable({ providedIn: 'root' })
export class SubscriptionService {
  private readonly http = inject(HttpClient);
  private readonly base = `${environment.apiBaseUrl}/subscription`;

  getPlans(): Observable<SubscriptionPlan[]> {
    return this.http.get<SubscriptionPlan[]>(`${this.base}/plans`);
  }

  selectFree(): Observable<void> {
    return this.http.post<void>(`${this.base}/select-free`, {});
  }

  createCheckout(priceId: string): Observable<{ checkoutUrl: string }> {
    return this.http.post<{ checkoutUrl: string }>(`${this.base}/checkout`, { priceId });
  }

  getCurrent(): Observable<CurrentSubscription> {
    return this.http.get<CurrentSubscription>(`${this.base}/current`);
  }
}
