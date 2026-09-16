import {
  cardBrandLabel, invoiceStatusKey, invoiceStatusVariant, isLiveSubscriptionEnded, liveSubscriptionStatusKey,
  liveSubscriptionStatusVariant, resolveBillingPeriod, trialRemaining,
} from './billing-display';

describe('billing-display', () => {
  describe('resolveBillingPeriod', () => {
    const now = new Date('2026-09-14T10:00:00Z');
    const stored = { currentPeriodStart: '2026-07-24T13:25:42Z', currentPeriodEnd: '2026-08-24T13:25:42Z' };
    const live = { currentPeriodStart: '2026-08-24T13:25:42Z', currentPeriodEnd: '2026-09-24T13:25:42Z' };

    it('★ Stripe\'s live period wins over the stored row (the missed-webhook case from runtime)', () => {
      expect(resolveBillingPeriod(live, stored, now)).toEqual({
        start: live.currentPeriodStart, end: live.currentPeriodEnd, source: 'stripe', stale: false,
      });
    });

    it('★ a stored period that already ended is flagged stale, never presented as current', () => {
      const p = resolveBillingPeriod(null, stored, now)!;
      expect(p.source).toBe('stored');
      expect(p.stale).toBeTrue();
    });

    it('a stored period still running is not stale', () => {
      expect(resolveBillingPeriod(null, live, now)!.stale).toBeFalse();
    });

    it('no dates anywhere → no period', () => {
      expect(resolveBillingPeriod(null, null, now)).toBeNull();
      expect(resolveBillingPeriod({ currentPeriodStart: null, currentPeriodEnd: null }, null, now)).toBeNull();
    });
  });

  it('trialRemaining is the share of the trial ahead, clamped, and null without a length', () => {
    expect(trialRemaining(7, 7)).toBe(1);
    expect(trialRemaining(3, 7)).toBeCloseTo(3 / 7, 5);
    expect(trialRemaining(10, 7)).toBe(1);
    expect(trialRemaining(0, 7)).toBe(0);
    expect(trialRemaining(3, null)).toBeNull();
    expect(trialRemaining(null, 7)).toBeNull();
  });

  it('★ live Stripe statuses map by whitelist onto the stored-status keys (§C2)', () => {
    expect(liveSubscriptionStatusKey('active')).toBe('SUBSCRIPTION.STATUS_ACTIVE');
    expect(liveSubscriptionStatusKey('past_due')).toBe('SUBSCRIPTION.STATUS_PASTDUE');
    expect(liveSubscriptionStatusKey('canceled')).toBe('SUBSCRIPTION.STATUS_CANCELED');
    expect(liveSubscriptionStatusKey('paused')).toBe('SUBSCRIPTION.STATUS_UNKNOWN');
    expect(liveSubscriptionStatusVariant('canceled')).toBe('danger');
    expect(liveSubscriptionStatusVariant('active')).toBe('success');
    expect(isLiveSubscriptionEnded('canceled')).toBeTrue();
    expect(isLiveSubscriptionEnded('incomplete_expired')).toBeTrue();
    expect(isLiveSubscriptionEnded('past_due')).toBeFalse();
  });

  it('card brands are named from a whitelist', () => {
    expect(cardBrandLabel('visa')).toBe('Visa');
    expect(cardBrandLabel('AMEX')).toBe('American Express');
    expect(cardBrandLabel('some_new_brand')).toBeNull();
    expect(cardBrandLabel(null)).toBeNull();
  });

  it('★ invoice status is a whitelist — an unexpected Stripe status never prints raw (§C2)', () => {
    expect(invoiceStatusKey('paid')).toBe('BILLING.INVOICE_STATUS_PAID');
    expect(invoiceStatusKey('draft')).toBe('BILLING.INVOICE_STATUS_UNKNOWN');
    expect(invoiceStatusKey(null)).toBe('BILLING.INVOICE_STATUS_UNKNOWN');
    expect(invoiceStatusVariant('paid')).toBe('success');
    expect(invoiceStatusVariant('open')).toBe('warning');
    expect(invoiceStatusVariant('uncollectible')).toBe('danger');
    expect(invoiceStatusVariant('whatever')).toBe('neutral');
  });
});
