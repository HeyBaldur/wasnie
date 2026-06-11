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

export interface ChangePlanResult {
  pending: boolean;
  blocked: boolean;
  blockedReason: string | null;
  current: number | null;
  limit: number | null;
  targetTier: string | null;
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

  changePlan(targetTier: string): Observable<ChangePlanResult> {
    return this.http.post<ChangePlanResult>(`${this.base}/change-plan`, { targetTier });
  }

  getBillingPortalUrl(): Observable<{ url: string }> {
    return this.http.post<{ url: string }>(`${this.base}/billing-portal`, {});
  }
}
