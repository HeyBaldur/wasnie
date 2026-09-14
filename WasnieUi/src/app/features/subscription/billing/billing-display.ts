import type { BadgeVariant } from '../../../shared/ui/ws-badge/ws-badge.component';

/** Card brands are proper names, not translated. Unknown brands fall back to a generic label (§C2). */
const CARD_BRANDS: Record<string, string> = {
  visa: 'Visa',
  mastercard: 'Mastercard',
  amex: 'American Express',
  discover: 'Discover',
  diners: 'Diners Club',
  jcb: 'JCB',
  unionpay: 'UnionPay',
  cartes_bancaires: 'Cartes Bancaires',
};

export function cardBrandLabel(brand: string | null | undefined): string | null {
  return brand ? CARD_BRANDS[brand.toLowerCase()] ?? null : null;
}

/** Stripe invoice status → translation key and badge. A whitelist: an unexpected status never prints raw. */
export function invoiceStatusKey(status: string | null | undefined): string {
  switch (status) {
    case 'paid': return 'BILLING.INVOICE_STATUS_PAID';
    case 'open': return 'BILLING.INVOICE_STATUS_OPEN';
    case 'uncollectible': return 'BILLING.INVOICE_STATUS_UNCOLLECTIBLE';
    case 'void': return 'BILLING.INVOICE_STATUS_VOID';
    default: return 'BILLING.INVOICE_STATUS_UNKNOWN';
  }
}

export function invoiceStatusVariant(status: string | null | undefined): BadgeVariant {
  switch (status) {
    case 'paid': return 'success';
    case 'open': return 'warning';
    case 'uncollectible': return 'danger';
    default: return 'neutral';
  }
}

/**
 * Stripe's live subscription status (snake_case) → the same keys the stored status uses. A whitelist (§C2).
 */
export function liveSubscriptionStatusKey(status: string | null | undefined): string {
  switch (status) {
    case 'active': return 'SUBSCRIPTION.STATUS_ACTIVE';
    case 'past_due':
    case 'unpaid': return 'SUBSCRIPTION.STATUS_PASTDUE';
    case 'canceled':
    case 'incomplete_expired': return 'SUBSCRIPTION.STATUS_CANCELED';
    case 'incomplete': return 'SUBSCRIPTION.STATUS_INCOMPLETE';
    case 'trialing': return 'SUBSCRIPTION.STATUS_TRIALING';
    default: return 'SUBSCRIPTION.STATUS_UNKNOWN';
  }
}

export function liveSubscriptionStatusVariant(status: string | null | undefined): BadgeVariant {
  switch (status) {
    case 'active':
    case 'trialing': return 'success';
    case 'past_due':
    case 'unpaid': return 'warning';
    case 'canceled':
    case 'incomplete':
    case 'incomplete_expired': return 'danger';
    default: return 'neutral';
  }
}

/** Stripe statuses after which the subscription no longer bills or renews. */
export function isLiveSubscriptionEnded(status: string | null | undefined): boolean {
  return status === 'canceled' || status === 'incomplete_expired';
}

export interface BillingPeriod {
  start: string | null;
  end: string | null;
  /** Where the dates came from: Stripe now, or the local row (only as fresh as the last webhook). */
  source: 'stripe' | 'stored';
  /** The stored period already ended and Stripe could not confirm a newer one: never shown as "renews on". */
  stale: boolean;
}

/**
 * ★ THE PERIOD THE CARD SHOWS. Stripe's live period wins; the stored one is the fallback when Stripe is unreachable.
 * A stored period whose end is already past is flagged `stale` instead of being presented as the current one —
 * that is exactly the "Renews on Aug 24" shown on September 14 (KAN-77, runtime).
 */
export function resolveBillingPeriod(
  live: { currentPeriodStart: string | null; currentPeriodEnd: string | null } | null,
  stored: { currentPeriodStart: string | null; currentPeriodEnd: string | null } | null,
  now: Date = new Date(),
): BillingPeriod | null {
  if (live?.currentPeriodEnd) {
    return { start: live.currentPeriodStart, end: live.currentPeriodEnd, source: 'stripe', stale: false };
  }
  if (stored?.currentPeriodEnd) {
    const end = new Date(stored.currentPeriodEnd).getTime();
    return {
      start: stored.currentPeriodStart,
      end: stored.currentPeriodEnd,
      source: 'stored',
      stale: Number.isFinite(end) && end <= now.getTime(),
    };
  }
  return null;
}

/** Share of the trial still ahead, 0..1; null when the length is unknown. */
export function trialRemaining(daysRemaining: number | null, lengthDays: number | null): number | null {
  if (daysRemaining == null || !lengthDays || lengthDays <= 0) return null;
  return Math.min(Math.max(daysRemaining / lengthDays, 0), 1);
}
