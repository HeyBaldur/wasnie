import { ComponentFixture, TestBed, fakeAsync, tick } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { provideTranslateService } from '@ngx-translate/core';
import { of, throwError, Subject } from 'rxjs';
import { SubscriptionReactivationComponent } from './subscription-reactivation.component';
import { SubscriptionService, CurrentSubscription, SubscriptionPlan, SubscriptionUsage } from '../services/subscription.service';
import { WsToastService } from '../../../shared/ui/ws-toast/ws-toast.service';

const CANCELED_SUB: CurrentSubscription = {
  tier: 'Growth', status: 'Canceled', billingEmail: 'test@test.com',
  stripeSubscriptionId: 'sub_x', stripeCustomerId: 'cus_x',
  stripePriceId: 'price_growth', stripeProductId: 'prod_growth',
  currentPeriodStart: null, currentPeriodEnd: null,
  nextBillingDate: null,
  canceledAt: '2026-06-12T10:00:00Z',
  cancelAtPeriodEnd: false, cancelAt: null,
  createdAt: '2026-01-01T00:00:00Z',
};

const PLANS: SubscriptionPlan[] = [
  {
    priceId: 'price_starter', productId: 'prod_starter',
    name: 'Starter', price: 29, currency: 'USD', interval: 'month',
    tier: 'Starter', maxPayees: 25, maxPlans: 5, isCurrentPlan: false,
  },
  {
    priceId: 'price_growth', productId: 'prod_growth',
    name: 'Growth', price: 79, currency: 'USD', interval: 'month',
    tier: 'Growth', maxPayees: 100, maxPlans: 20, isCurrentPlan: false,
  },
];

describe('SubscriptionReactivationComponent', () => {
  let fixture: ComponentFixture<SubscriptionReactivationComponent>;
  let component: SubscriptionReactivationComponent;
  let subscriptionMock: jasmine.SpyObj<SubscriptionService>;
  let toastMock: jasmine.SpyObj<WsToastService>;

  beforeEach(async () => {
    subscriptionMock = jasmine.createSpyObj('SubscriptionService', ['getCurrent', 'getPlans', 'getUsage', 'createCheckout']);
    toastMock = jasmine.createSpyObj('WsToastService', ['show']);

    subscriptionMock.getCurrent.and.returnValue(of(CANCELED_SUB));
    subscriptionMock.getPlans.and.returnValue(of(PLANS));
    subscriptionMock.getUsage.and.returnValue(of({ payeeCount: 0, planCount: 0 } as SubscriptionUsage));
    subscriptionMock.createCheckout.and.returnValue(of({ checkoutUrl: 'https://checkout.stripe.com/test' }));

    await TestBed.configureTestingModule({
      imports: [SubscriptionReactivationComponent],
      providers: [
        { provide: SubscriptionService, useValue: subscriptionMock },
        { provide: WsToastService, useValue: toastMock },
        provideRouter([]),
        provideTranslateService({ defaultLanguage: 'en' }),
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(SubscriptionReactivationComponent);
    component = fixture.componentInstance;
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  it('should load subscription, plans, and usage on init', fakeAsync(() => {
    fixture.detectChanges();
    tick();

    expect(subscriptionMock.getCurrent).toHaveBeenCalled();
    expect(subscriptionMock.getPlans).toHaveBeenCalled();
    expect(subscriptionMock.getUsage).toHaveBeenCalled();
    expect(component.subscription()).toEqual(CANCELED_SUB);
    expect(component.plans().length).toBe(2);
    expect(component.usage()).toEqual({ payeeCount: 0, planCount: 0 });
    expect(component.loading()).toBeFalse();
  }));

  it('should filter out Free plans from the plan list', fakeAsync(() => {
    const plansWithFree: SubscriptionPlan[] = [
      ...PLANS,
      { priceId: null, productId: null, name: 'Free', price: 0, currency: 'USD', interval: 'month', tier: 'Free', maxPayees: 5, maxPlans: 2, isCurrentPlan: false },
    ];
    subscriptionMock.getPlans.and.returnValue(of(plansWithFree));

    fixture.detectChanges();
    tick();

    expect(component.plans().every(p => p.tier !== 'Free')).toBeTrue();
  }));

  it('should identify previous plan correctly', fakeAsync(() => {
    fixture.detectChanges();
    tick();

    const growthPlan = component.plans().find(p => p.tier === 'Growth')!;
    const starterPlan = component.plans().find(p => p.tier === 'Starter')!;

    expect(component.isPreviousPlan(growthPlan)).toBeTrue();
    expect(component.isPreviousPlan(starterPlan)).toBeFalse();
  }));

  it('should call createCheckout when reactivate() is called', () => {
    // Use a subject so window.location.assign is never reached (avoids browser navigation in tests)
    const subject = new Subject<{ checkoutUrl: string }>();
    subscriptionMock.createCheckout.and.returnValue(subject);

    component.reactivate('price_growth', 'Growth');

    expect(subscriptionMock.createCheckout).toHaveBeenCalledWith('price_growth');
    subject.complete();
  });

  it('should set startingCheckout signal synchronously when checkout begins', () => {
    const subject = new Subject<{ checkoutUrl: string }>();
    subscriptionMock.createCheckout.and.returnValue(subject);

    component.reactivate('price_growth', 'Growth');

    expect(component.startingCheckout()).toBe('Growth');
    expect(component.isStartingCheckoutFor('Growth')).toBeTrue();
    expect(component.isStartingCheckoutFor('Starter')).toBeFalse();

    // Complete the subject so no open subscriptions remain
    subject.complete();
  });

  it('should show error toast when checkout fails', fakeAsync(() => {
    subscriptionMock.createCheckout.and.returnValue(throwError(() => new Error('network')));
    fixture.detectChanges();
    tick();

    component.reactivate('price_growth', 'Growth');
    tick();

    expect(toastMock.show).toHaveBeenCalledWith('SUBSCRIPTION.REACTIVATION_CHECKOUT_ERROR', 'error');
    expect(component.startingCheckout()).toBeNull();
  }));

  it('should set loadError when getCurrent fails', fakeAsync(() => {
    subscriptionMock.getCurrent.and.returnValue(throwError(() => new Error('network')));

    fixture.detectChanges();
    tick();

    expect(component.loadError()).toBeTrue();
    expect(component.loading()).toBeFalse();
  }));

  describe('isPlanBlocked* — tier limit enforcement', () => {
    const STARTER: SubscriptionPlan = {
      priceId: 'price_starter', productId: 'prod_starter',
      name: 'Starter', price: 29, currency: 'USD', interval: 'month',
      tier: 'Starter', maxPayees: 25, maxPlans: 5, isCurrentPlan: false,
    };
    const UNLIMITED: SubscriptionPlan = {
      priceId: 'price_scale', productId: 'prod_scale',
      name: 'Scale', price: 199, currency: 'USD', interval: 'month',
      tier: 'Scale', maxPayees: -1, maxPlans: -1, isCurrentPlan: false,
    };

    it('isPlanBlocked is false when usage is null', () => {
      component.usage.set(null);
      expect(component.isPlanBlocked(STARTER)).toBeFalse();
    });

    it('isPlanBlockedByPayees is false when payee count fits', () => {
      component.usage.set({ payeeCount: 25, planCount: 0 });
      expect(component.isPlanBlockedByPayees(STARTER)).toBeFalse();
    });

    it('isPlanBlockedByPayees is true when payee count exceeds limit', () => {
      component.usage.set({ payeeCount: 26, planCount: 0 });
      expect(component.isPlanBlockedByPayees(STARTER)).toBeTrue();
    });

    it('isPlanBlockedByPlans is true when plan count exceeds limit', () => {
      component.usage.set({ payeeCount: 0, planCount: 6 });
      expect(component.isPlanBlockedByPlans(STARTER)).toBeTrue();
    });

    it('isPlanBlocked is true when either limit is exceeded', () => {
      component.usage.set({ payeeCount: 30, planCount: 0 });
      expect(component.isPlanBlocked(STARTER)).toBeTrue();
    });

    it('isPlanBlockedByPayees is always false for unlimited plans (maxPayees = -1)', () => {
      component.usage.set({ payeeCount: 9999, planCount: 9999 });
      expect(component.isPlanBlockedByPayees(UNLIMITED)).toBeFalse();
      expect(component.isPlanBlockedByPlans(UNLIMITED)).toBeFalse();
      expect(component.isPlanBlocked(UNLIMITED)).toBeFalse();
    });
  });
});
