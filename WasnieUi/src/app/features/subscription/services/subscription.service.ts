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
  /** The plan code from the server's catalog (e.g. "pro"). KAN-77 replaced the old Free/Starter/Growth/Scale tier. */
  planCode: string;
  /** -1 = unlimited. */
  maxPayees: number;
  /** -1 = unlimited. */
  maxPlans: number;
  isCurrentPlan: boolean;
}

export interface CurrentSubscription {
  planCode: string | null;
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
  cancelAtPeriodEnd: boolean;
  cancelAt: string | null;
  createdAt: string;
}

export type AccountAccessState = 'Trial' | 'Active' | 'Locked';
export type AccountLockReason = 'TrialEnded' | 'SubscriptionEnded' | 'NoSubscription';

/**
 * KAN-77 — whether the account can use the product, derived on the server from the trial end and the
 * subscription. The trial banner, the paywall and the guards all read this one response.
 */
export interface AccountAccess {
  state: AccountAccessState;
  lockReason: AccountLockReason | null;
  trialEndsAt: string | null;
  /** Whole days left, rounded up by the server. Null unless in trial. */
  trialDaysRemaining: number | null;
  /** The configured trial length — what "N days left" is out of. Null unless in trial. */
  trialLengthDays: number | null;
  /**
   * KAN-80 — assistant tokens consumed (input + output). Trial: since the account began. Active: in the current billing
   * period. Null when locked.
   */
  assistantTokensUsed: number | null;
  /** The trial's token allowance. Null for a paying account, which has none. */
  assistantTokenLimit: number | null;
  /** Start of the current billing period for a paying account; null for a trial and when locked. */
  assistantTokensSince: string | null;
}

export interface SubscriptionUsage {
  payeeCount: number;
  planCount: number;
}

/** Card on file, read live from Stripe. Brand/last4 are null for methods that are not cards. */
export interface BillingPaymentMethod {
  type: string;
  brand: string | null;
  last4: string | null;
  expMonth: number | null;
  expYear: number | null;
}

/** One Stripe invoice. `status` is Stripe's raw value (open, paid, uncollectible, void) — map it by whitelist. */
export interface BillingInvoice {
  id: string;
  number: string | null;
  description: string | null;
  createdAt: string;
  total: number;
  currency: string;
  status: string | null;
  hostedInvoiceUrl: string | null;
  invoicePdfUrl: string | null;
}

/**
 * The subscription as Stripe has it NOW. The local row only moves when a webhook arrives, so a missed renewal leaves
 * it on an expired period; the billing card prefers this. `status` is Stripe's raw value — map it by whitelist.
 */
export interface BillingLiveSubscription {
  status: string;
  currentPeriodStart: string | null;
  currentPeriodEnd: string | null;
  cancelAtPeriodEnd: boolean;
  cancelAt: string | null;
  endedAt: string | null;
}

export interface BillingDetails {
  subscription: BillingLiveSubscription | null;
  paymentMethod: BillingPaymentMethod | null;
  invoices: BillingInvoice[];
  /**
   * True when reading Stripe found the stored subscription out of date (a missed webhook) and the server corrected
   * it. The account's access may have changed — reload it.
   */
  synced: boolean;
}

export interface ChangePlanResult {
  pending: boolean;
  blocked: boolean;
  blockedReason: string | null;
  current: number | null;
  limit: number | null;
  targetPlanCode: string | null;
}

@Injectable({ providedIn: 'root' })
export class SubscriptionService {
  private readonly http = inject(HttpClient);
  private readonly base = `${environment.apiBaseUrl}/subscription`;

  getPlans(): Observable<SubscriptionPlan[]> {
    return this.http.get<SubscriptionPlan[]>(`${this.base}/plans`);
  }

  createCheckout(priceId: string): Observable<{ checkoutUrl: string }> {
    return this.http.post<{ checkoutUrl: string }>(`${this.base}/checkout`, { priceId });
  }

  getCurrent(): Observable<CurrentSubscription> {
    return this.http.get<CurrentSubscription>(`${this.base}/current`);
  }

  getAccess(): Observable<AccountAccess> {
    return this.http.get<AccountAccess>(`${this.base}/access`);
  }

  getUsage(): Observable<SubscriptionUsage> {
    return this.http.get<SubscriptionUsage>(`${this.base}/usage`);
  }

  changePlan(targetPlanCode: string): Observable<ChangePlanResult> {
    return this.http.post<ChangePlanResult>(`${this.base}/change-plan`, { targetPlanCode });
  }

  getBillingDetails(): Observable<BillingDetails> {
    return this.http.get<BillingDetails>(`${this.base}/billing-details`);
  }

  getBillingPortalUrl(): Observable<{ url: string }> {
    return this.http.post<{ url: string }>(`${this.base}/billing-portal`, {});
  }

  revertCancellation(): Observable<void> {
    return this.http.post<void>(`${this.base}/revert-cancellation`, {});
  }
}
