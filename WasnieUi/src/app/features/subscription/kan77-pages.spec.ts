import { HttpErrorResponse } from '@angular/common/http';
import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { TranslateModule } from '@ngx-translate/core';
import { of, throwError } from 'rxjs';
import { CurrentUserService } from '../../core/auth/current-user.service';
import { AuthService } from '../../core/services/auth.service';
import { WsToastService } from '../../shared/ui/ws-toast/ws-toast.service';
import { SubscriptionStateService } from './services/subscription-state.service';
import { AccountAccess, BoostOffer, CurrentSubscription, SubscriptionPlan, SubscriptionService, BillingDetails } from './services/subscription.service';
import { PlanOfferComponent } from './plan-offer/plan-offer.component';
import { checkoutNavigator } from './plan-offer/start-checkout';
import { PaywallComponent } from './paywall/paywall.component';
import { ManageBillingComponent, subscriptionStatusKey } from './billing/manage-billing.component';
import { AppShellComponent } from '../../shared/components/app-shell/app-shell.component';

/** The real shell pulls the whole app (inactivity, assistant, sidebar…); the page is the subject here. */
@Component({ selector: 'app-shell', standalone: true, template: '<ng-content />' })
class StubShellComponent {}

/**
 * KAN-77 — the three pages that split the old subscription screen: the plan offer (Pricing and paywall share it),
 * the paywall, and Manage billing.
 */
const PRO: SubscriptionPlan = {
  priceId: 'price_1Th4TS3kwCZf9lCAk5YpI76X', productId: 'prod_UgRMfSuRxZA3H4', name: 'Incentra Pro',
  price: 299, currency: 'EUR', interval: 'month', planCode: 'pro', maxPayees: -1, maxPlans: -1, isCurrentPlan: false,
};

const access = (state: AccountAccess['state'], lockReason: AccountAccess['lockReason'] = null): AccountAccess => ({
  state, lockReason, trialEndsAt: '2026-09-21T10:59:45Z', trialDaysRemaining: state === 'Trial' ? 7 : null, trialLengthDays: state === 'Trial' ? 7 : null,
  assistantTokensUsed: state === 'Locked' ? null : 12_000,
  // KAN-83: a paying account has an allowance of its own now — it is only null when locked.
  assistantTokenLimit: state === 'Locked' ? null : (state === 'Trial' ? 1_000_000 : 3_000_000),
  assistantTokensSince: state === 'Active' ? '2026-09-01T00:00:00Z' : null,
  assistantBoostRemaining: 0,
  assistantBoostExpiresAt: null,
  assistantBoostExpired: 0,
});

const toast = () => jasmine.createSpyObj<WsToastService>('WsToastService', ['show']);

describe('KAN-77 · PlanOfferComponent', () => {
  const render = (plan: SubscriptionPlan, inputs: { current?: boolean; canSubscribe?: boolean } = {}) => {
    TestBed.configureTestingModule({ imports: [PlanOfferComponent, TranslateModule.forRoot()], providers: [provideRouter([])] });
    const fixture = TestBed.createComponent(PlanOfferComponent);
    fixture.componentRef.setInput('plan', plan);
    if (inputs.current !== undefined) fixture.componentRef.setInput('current', inputs.current);
    if (inputs.canSubscribe !== undefined) fixture.componentRef.setInput('canSubscribe', inputs.canSubscribe);
    fixture.detectChanges();
    return fixture;
  };

  it('★ an unlimited plan says so, from the plan itself — no comparison table', () => {
    const el: HTMLElement = render(PRO).nativeElement;

    expect(el.textContent).toContain('PRICING.INCLUDED_PAYEES_UNLIMITED');
    expect(el.textContent).toContain('PRICING.INCLUDED_PLANS_UNLIMITED');
    expect(el.querySelector('table')).toBeNull();
  });

  it('a capped plan shows its cap (the multi-plan future, from the same card)', () => {
    const el: HTMLElement = render({ ...PRO, maxPayees: 25, maxPlans: 5 }).nativeElement;

    expect(el.textContent).toContain('PRICING.INCLUDED_PAYEES_LIMITED');
    expect(el.textContent).not.toContain('PRICING.INCLUDED_PAYEES_UNLIMITED');
  });

  it('the current plan is badged and has no subscribe button; no permission, no button either', () => {
    const current: HTMLElement = render(PRO, { current: true }).nativeElement;
    expect(current.textContent).toContain('PRICING.CURRENT_PLAN');
    expect(current.querySelector('ws-button')).toBeNull();

    TestBed.resetTestingModule();
    expect(render(PRO, { canSubscribe: false }).nativeElement.querySelector('ws-button')).toBeNull();
  });

  it('subscribing emits the plan', () => {
    const fixture = render(PRO);
    let emitted: SubscriptionPlan | undefined;
    fixture.componentInstance.subscribe.subscribe(p => (emitted = p));

    (fixture.nativeElement.querySelector('ws-button button') as HTMLButtonElement).click();

    expect(emitted).toEqual(PRO);
  });
});

describe('KAN-77 · PaywallComponent', () => {
  let service: jasmine.SpyObj<SubscriptionService>;
  let toastSpy: jasmine.SpyObj<WsToastService>;
  let goSpy: jasmine.Spy;

  const render = (acc: AccountAccess) => {
    service = jasmine.createSpyObj<SubscriptionService>('SubscriptionService', ['getAccess', 'getPlans', 'createCheckout']);
    service.getAccess.and.returnValue(of(acc));
    service.getPlans.and.returnValue(of([PRO]));
    toastSpy = toast();
    TestBed.configureTestingModule({
      imports: [PaywallComponent, TranslateModule.forRoot()],
      providers: [
        provideRouter([]),
        { provide: SubscriptionService, useValue: service },
        { provide: WsToastService, useValue: toastSpy },
        { provide: AuthService, useValue: { logout: () => {} } },
        { provide: CurrentUserService, useValue: { hasPermission: () => true } },
      ],
    });
    const router = TestBed.inject(Router);
    spyOn(router, 'navigateByUrl').and.resolveTo(true);
    goSpy = spyOn(checkoutNavigator, 'go');
    const fixture = TestBed.createComponent(PaywallComponent);
    fixture.detectChanges();
    return { fixture, router };
  };

  it('names the reason with an explicit key, and says the data is kept', () => {
    const { fixture } = render(access('Locked', 'TrialEnded'));
    const el: HTMLElement = fixture.nativeElement;

    expect(fixture.componentInstance.titleKey()).toBe('PAYWALL.TITLE_TRIAL_ENDED');
    expect(el.textContent).toContain('PAYWALL.DATA_KEPT');
    expect(el.querySelectorAll('app-plan-offer').length).toBe(1);
  });

  it('a subscription that ended gets its own title', () => {
    const { fixture } = render(access('Locked', 'SubscriptionEnded'));
    expect(fixture.componentInstance.titleKey()).toBe('PAYWALL.TITLE_SUBSCRIPTION_ENDED');
  });

  it('an account that is not locked (it paid meanwhile) is sent to the dashboard', () => {
    const { router } = render(access('Active'));
    expect(router.navigateByUrl).toHaveBeenCalledWith('/dashboard');
    expect(service.getPlans).not.toHaveBeenCalled();
  });

  it('★ a tenant that already pays is told so — never sent to a second checkout (KAN-77)', () => {
    const { fixture } = render(access('Locked', 'SubscriptionEnded'));
    service.createCheckout.and.returnValue(throwError(() => new HttpErrorResponse({
      status: 409, error: { blocked: true, blockedReason: 'AlreadySubscribed' },
    })));
    fixture.componentInstance.subscribe(PRO);
    expect(toastSpy.show).toHaveBeenCalledWith('PRICING.CHECKOUT_ALREADY_SUBSCRIBED', 'error');
    expect(fixture.componentInstance.subscribing()).toBeNull();
  });

  it('subscribing goes to Stripe Checkout; a failure tells the user and stops the spinner', () => {
    const { fixture } = render(access('Locked', 'TrialEnded'));
    service.createCheckout.and.returnValue(of({ checkoutUrl: 'https://checkout.stripe.com/c/pay/x' }));

    fixture.componentInstance.subscribe(PRO);
    expect(goSpy).toHaveBeenCalledWith('https://checkout.stripe.com/c/pay/x');

    fixture.componentInstance.subscribing.set(null);
    service.createCheckout.and.returnValue(throwError(() => new HttpErrorResponse({ status: 503 })));
    fixture.componentInstance.subscribe(PRO);
    expect(toastSpy.show).toHaveBeenCalledWith('PRICING.CHECKOUT_ERROR', 'error');
    expect(fixture.componentInstance.subscribing()).toBeNull();
  });
});

describe('KAN-77 · ManageBillingComponent', () => {
  const SUB: CurrentSubscription = {
    planCode: 'pro', status: 'Active', billingEmail: 'billing@acme.com', stripeSubscriptionId: 'sub_1',
    stripeCustomerId: 'cus_1', stripePriceId: PRO.priceId, stripeProductId: PRO.productId,
    currentPeriodStart: '2026-09-01T00:00:00Z', currentPeriodEnd: '2026-10-01T00:00:00Z', nextBillingDate: '2026-10-01T00:00:00Z',
    canceledAt: null, cancelAtPeriodEnd: false, cancelAt: null, createdAt: '2026-09-01T00:00:00Z',
  };

  const EMPTY: BillingDetails = { subscription: null, paymentMethod: null, invoices: [], synced: false };
  const LIVE_ACTIVE = {
    status: 'active', currentPeriodStart: '2026-08-24T13:25:42Z', currentPeriodEnd: '2099-09-24T13:25:42Z',
    cancelAtPeriodEnd: false, cancelAt: null, endedAt: null,
  };

  const render = (acc: AccountAccess, sub: CurrentSubscription | null, details: BillingDetails = { subscription: null, paymentMethod: null, invoices: [], synced: false }) => {
    const service = jasmine.createSpyObj<SubscriptionService>('SubscriptionService',
      ['getAccess', 'getPlans', 'getCurrent', 'getUsage', 'getBillingDetails', 'getBillingPortalUrl',
        'revertCancellation', 'getBoostOffers', 'createBoostCheckout']);
    // KAN-83: no packs on sale by default — the boost card is absent unless a test puts something in the shop.
    service.getBoostOffers.and.returnValue(of([]));
    service.getUsage.and.returnValue(of({ payeeCount: 3, planCount: 2 }));
    service.getBillingDetails.and.returnValue(of(details));
    service.getAccess.and.returnValue(of(acc));
    service.getPlans.and.returnValue(of([PRO]));
    service.getCurrent.and.returnValue(sub ? of(sub) : throwError(() => new HttpErrorResponse({ status: 404 })));
    TestBed.configureTestingModule({
      imports: [ManageBillingComponent, TranslateModule.forRoot()],
      providers: [
        provideRouter([]),
        { provide: SubscriptionService, useValue: service },
        { provide: WsToastService, useValue: toast() },
        { provide: CurrentUserService, useValue: { hasPermission: () => true, currentUser: () => null } },
        {
          provide: SubscriptionStateService,
          useValue: {
            load: () => {}, refresh: jasmine.createSpy('refresh'), subscription: signal(null), access: signal(null), isPastDue: signal(false),
            isTrial: signal(false), isLocked: signal(false), trialDaysRemaining: signal(null),
          },
        },
      ],
    });
    TestBed.overrideComponent(ManageBillingComponent, {
      remove: { imports: [AppShellComponent] },
      add: { imports: [StubShellComponent] },
    });
    const fixture = TestBed.createComponent(ManageBillingComponent);
    fixture.detectChanges();
    return fixture;
  };

  it('★ a trial with no subscription (404) is a normal state: the trial card, not an error', () => {
    const fixture = render(access('Trial'), null);
    const el: HTMLElement = fixture.nativeElement;

    expect(fixture.componentInstance.loadError()).toBeFalse();
    expect(el.textContent).toContain('BILLING.TRIAL_TITLE');
    // KAN-80: the trial's assistant allowance is now a token meter, not a message count.
    expect(el.querySelector('[data-testid="token-usage-trial"]')).not.toBeNull();
    expect(el.textContent).not.toContain('SUBSCRIPTION.BILLING_PORTAL_BTN');
  });

  it('a paying account sees its plan by name, its status and the billing portal', () => {
    const fixture = render(access('Active'), SUB);
    const el: HTMLElement = fixture.nativeElement;

    expect(fixture.componentInstance.planName()).toBe('Incentra Pro');
    expect(el.textContent).toContain('SUBSCRIPTION.STATUS_ACTIVE');
    expect(el.textContent).toContain('SUBSCRIPTION.BILLING_PORTAL_BTN');
    expect(el.textContent).not.toContain('BILLING.TRIAL_TITLE');
  });

  it('★ the trial card shows the countdown ring; the paying card never does (KAN-77 runtime)', () => {
    const trial = render(access('Trial'), null);
    expect(trial.nativeElement.querySelector('.billing-ring')).not.toBeNull();
    expect(trial.componentInstance.cardState()).toBe('trial');
    TestBed.resetTestingModule();

    const paid = render(access('Active'), SUB, { ...EMPTY, subscription: LIVE_ACTIVE });
    const el: HTMLElement = paid.nativeElement;
    expect(paid.componentInstance.cardState()).toBe('paid');
    expect(el.querySelector('.billing-ring')).toBeNull();
    expect(el.textContent).not.toContain('BILLING.TRIAL_DAYS_LEFT');
    expect(el.textContent).toContain('BILLING.RENEWS_ON');
  });

  it('★ the renewal date comes from Stripe\'s live period, not the stale stored row', () => {
    const stale = { ...SUB, currentPeriodStart: '2026-07-24T13:25:42Z', currentPeriodEnd: '2026-08-24T13:25:42Z' };
    const fixture = render(access('Active'), stale, { ...EMPTY, subscription: LIVE_ACTIVE });
    expect(fixture.componentInstance.period()!.end).toBe(LIVE_ACTIVE.currentPeriodEnd);
    expect(fixture.componentInstance.period()!.stale).toBeFalse();
  });

  it('★ without Stripe, an expired stored period is not shown as "renews on"', () => {
    const stale = { ...SUB, currentPeriodStart: '2020-01-01T00:00:00Z', currentPeriodEnd: '2020-02-01T00:00:00Z' };
    const fixture = render(access('Active'), stale);
    const el: HTMLElement = fixture.nativeElement;
    expect(el.textContent).toContain('BILLING.RENEWAL_UNCONFIRMED');
    expect(el.textContent).not.toContain('BILLING.RENEWS_ON');
  });

  it('★ Stripe says canceled while the stored row says Active: the card shows it ended', () => {
    const fixture = render(access('Active'), SUB, {
      ...EMPTY, subscription: { ...LIVE_ACTIVE, status: 'canceled', endedAt: '2026-09-13T09:38:33Z' },
    });
    const el: HTMLElement = fixture.nativeElement;
    expect(fixture.componentInstance.cardState()).toBe('ended');
    expect(el.textContent).toContain('SUBSCRIPTION.STATUS_CANCELED');
    expect(el.textContent).toContain('BILLING.ENDED_TITLE');
    expect(el.textContent).not.toContain('BILLING.RENEWS_ON');
  });

  it('★ when the server synced a missed cancellation, the app state is refreshed and a locked account goes to the paywall', () => {
    const fixture = render(access('Active'), SUB, {
      ...EMPTY, synced: true, subscription: { ...LIVE_ACTIVE, status: 'canceled', endedAt: '2026-09-13T09:38:33Z' },
    });
    const state = TestBed.inject(SubscriptionStateService) as unknown as { refresh: jasmine.Spy };
    const service = TestBed.inject(SubscriptionService) as jasmine.SpyObj<SubscriptionService>;
    expect(state.refresh).toHaveBeenCalled();
    expect(service.getAccess).toHaveBeenCalledTimes(2);
    expect(fixture.componentInstance.cardState()).toBe('ended');
  });

  it('no payment method is an empty state with an add action, not an error', () => {
    const fixture = render(access('Active'), SUB);
    const el: HTMLElement = fixture.nativeElement;
    expect(el.textContent).toContain('BILLING.NO_PAYMENT_METHOD');
    expect(el.textContent).toContain('BILLING.ADD_PAYMENT_METHOD');
    expect(el.textContent).not.toContain('BILLING.DETAILS_ERROR');
  });

  it('a scheduled cancellation offers to keep the subscription (classic and flexible billing)', () => {
    expect(render(access('Active'), { ...SUB, cancelAtPeriodEnd: true }).componentInstance.isCancelScheduled()).toBeTrue();
    TestBed.resetTestingModule();
    expect(render(access('Active'), { ...SUB, cancelAt: '2026-10-01T00:00:00Z' }).componentInstance.isCancelScheduled()).toBeTrue();
  });

  it('shows the card on file and the invoices read from Stripe, with a whitelisted invoice status', () => {
    const fixture = render(access('Active'), SUB, {
      subscription: null, synced: false,
      paymentMethod: { type: 'card', brand: 'visa', last4: '4242', expMonth: 3, expYear: 2028 },
      invoices: [{
        id: 'in_1', number: 'A1-0001', description: 'Incentra Pro', createdAt: '2026-09-01T08:00:00Z', total: 299,
        currency: 'EUR', status: 'paid', hostedInvoiceUrl: 'https://invoice', invoicePdfUrl: 'https://pdf',
      }],
    });
    const el: HTMLElement = fixture.nativeElement;

    expect(el.textContent).toContain('Visa');
    expect(el.textContent).toContain('4242');
    expect(el.textContent).toContain('A1-0001');
    expect(el.textContent).toContain('BILLING.INVOICE_STATUS_PAID');
  });

  it('★ a Stripe failure empties only the Stripe sections — the subscription card still renders', () => {
    TestBed.resetTestingModule();
    const fixture = render(access('Active'), SUB);
    const service = TestBed.inject(SubscriptionService) as jasmine.SpyObj<SubscriptionService>;
    service.getBillingDetails.and.returnValue(throwError(() => new HttpErrorResponse({ status: 400 })));
    fixture.componentInstance.loadDetails();
    fixture.detectChanges();
    const el: HTMLElement = fixture.nativeElement;

    expect(fixture.componentInstance.loadError()).toBeFalse();
    expect(el.textContent).toContain('BILLING.DETAILS_ERROR');
    expect(el.textContent).toContain('Incentra Pro');
  });

  // ── KAN-83 UX: the add-on is a quiet row, and the purchase lives in a dialog ───

  const withOffers = (acc: AccountAccess, sub: CurrentSubscription | null, offers: BoostOffer[]) => {
    TestBed.resetTestingModule();
    const fixture = render(acc, sub);
    const service = TestBed.inject(SubscriptionService) as jasmine.SpyObj<SubscriptionService>;
    service.getBoostOffers.and.returnValue(of(offers));
    fixture.componentInstance.load();
    fixture.detectChanges();
    return { fixture, service };
  };

  const PACKS: BoostOffer[] = [
    { priceId: 'price_3m', tokens: 3_000_000, amountCents: 2000, currency: 'eur' },
    { priceId: 'price_12m', tokens: 12_000_000, amountCents: 4000, currency: 'eur' },
  ];

  it('KAN-83 · a paying account gets a discreet add-on row with a secondary action', () => {
    const { fixture } = withOffers(access('Active'), SUB, PACKS);
    const el: HTMLElement = fixture.nativeElement;

    expect(el.querySelector('#add-tokens')).not.toBeNull();
    expect(el.textContent).toContain('ASSISTANT_USAGE.ADDONS_TITLE');
    expect(el.querySelector('[data-testid="billing-add-tokens"]')).not.toBeNull();
  });

  it('KAN-83 · ★ the packs and prices are NOT on the billing screen — they live in the dialog', () => {
    // The whole point of the redesign: this page reads as administration, not as a sales counter.
    const { fixture } = withOffers(access('Active'), SUB, PACKS);
    const el: HTMLElement = fixture.nativeElement;

    expect(el.textContent).not.toContain('3,000,000');
    expect(el.textContent).not.toContain('12,000,000');
    expect(fixture.componentInstance.addTokensOpen()).toBeFalse();
  });

  it('KAN-83 · the row opens the purchase dialog', () => {
    const { fixture } = withOffers(access('Active'), SUB, PACKS);

    fixture.componentInstance.openAddTokens();

    expect(fixture.componentInstance.addTokensOpen()).toBeTrue();
  });

  it('KAN-83 · ★ a TRIAL is never offered add-ons — its way forward is to subscribe', () => {
    const { fixture } = withOffers(access('Trial'), null, PACKS);

    expect((fixture.nativeElement as HTMLElement).querySelector('#add-tokens')).toBeNull();
  });

  it('KAN-83 · ★ no packs configured means no row at all, not an empty one', () => {
    const { fixture } = withOffers(access('Active'), SUB, []);

    expect((fixture.nativeElement as HTMLElement).querySelector('#add-tokens')).toBeNull();
  });

  it('KAN-83 · ★ a Stripe failure listing packs hides the row and leaves the page intact', () => {
    TestBed.resetTestingModule();
    const fixture = render(access('Active'), SUB);
    const service = TestBed.inject(SubscriptionService) as jasmine.SpyObj<SubscriptionService>;
    service.getBoostOffers.and.returnValue(throwError(() => new HttpErrorResponse({ status: 500 })));
    fixture.componentInstance.load();
    fixture.detectChanges();
    const el: HTMLElement = fixture.nativeElement;

    expect(el.querySelector('#add-tokens')).toBeNull();
    expect(fixture.componentInstance.loadError()).toBeFalse();
    expect(el.textContent).toContain('Incentra Pro');
  });

  it('KAN-83 · ★ no visible copy on this screen says "boost"', () => {
    const { fixture } = withOffers(access('Active'), SUB, PACKS);

    expect((fixture.nativeElement as HTMLElement).textContent!.toLowerCase()).not.toContain('boost');
  });

  it('★ the status key is a whitelist — an unknown Stripe status never prints an identifier (§C2)', () => {
    expect(subscriptionStatusKey('PastDue')).toBe('SUBSCRIPTION.STATUS_PASTDUE');
    expect(subscriptionStatusKey('paused_by_stripe')).toBe('SUBSCRIPTION.STATUS_UNKNOWN');
    expect(subscriptionStatusKey(null)).toBe('SUBSCRIPTION.STATUS_UNKNOWN');
  });
});
