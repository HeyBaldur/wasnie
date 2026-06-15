import { ComponentFixture, TestBed, fakeAsync, tick } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { provideTranslateService } from '@ngx-translate/core';
import { of } from 'rxjs';
import { ManageSubscriptionComponent } from './manage-subscription.component';
import { SubscriptionService, CurrentSubscription, SubscriptionPlan } from '../services/subscription.service';
import { WsToastService } from '../../../shared/ui/ws-toast/ws-toast.service';

function makeSub(overrides: Partial<CurrentSubscription> = {}): CurrentSubscription {
  return {
    tier: 'Growth',
    status: 'Active',
    billingEmail: 'test@test.com',
    stripeSubscriptionId: 'sub_x',
    stripeCustomerId: 'cus_x',
    stripePriceId: 'price_growth',
    stripeProductId: 'prod_growth',
    currentPeriodStart: null,
    currentPeriodEnd: null,
    nextBillingDate: null,
    canceledAt: null,
    cancelAtPeriodEnd: false,
    cancelAt: null,
    createdAt: '2026-01-01T00:00:00Z',
    ...overrides,
  };
}

const PLANS: SubscriptionPlan[] = [];

describe('ManageSubscriptionComponent', () => {
  let subscriptionMock: jasmine.SpyObj<SubscriptionService>;
  let toastMock: jasmine.SpyObj<WsToastService>;

  beforeEach(async () => {
    subscriptionMock = jasmine.createSpyObj('SubscriptionService', [
      'getCurrent', 'getPlans', 'changePlan', 'createCheckout', 'getBillingPortalUrl', 'revertCancellation',
    ]);
    toastMock = jasmine.createSpyObj('WsToastService', ['show']);
    subscriptionMock.getPlans.and.returnValue(of(PLANS));

    await TestBed.configureTestingModule({
      imports: [ManageSubscriptionComponent],
      providers: [
        { provide: SubscriptionService, useValue: subscriptionMock },
        { provide: WsToastService, useValue: toastMock },
        provideRouter([]),
        provideTranslateService({ defaultLanguage: 'en' }),
      ],
    }).overrideComponent(ManageSubscriptionComponent, {
      set: { template: '<div></div>', imports: [] },
    }).compileComponents();
  });

  // ── Canceled redirect ──────────────────────────────────────────────────────

  it('should redirect to /subscription/reactivate when getCurrent returns Canceled', fakeAsync(() => {
    subscriptionMock.getCurrent.and.returnValue(
      of(makeSub({ status: 'Canceled', canceledAt: '2026-06-12T10:00:00Z' })),
    );
    const fixture: ComponentFixture<ManageSubscriptionComponent> = TestBed.createComponent(ManageSubscriptionComponent);
    const navigateSpy = spyOn(TestBed.inject(Router), 'navigate').and.returnValue(Promise.resolve(true));
    fixture.detectChanges();
    tick();
    expect(navigateSpy).toHaveBeenCalledWith(['/subscription/reactivate']);
  }));

  it('should not load plans when status is Canceled', fakeAsync(() => {
    subscriptionMock.getCurrent.and.returnValue(
      of(makeSub({ status: 'Canceled', canceledAt: '2026-06-12T10:00:00Z' })),
    );
    const fixture: ComponentFixture<ManageSubscriptionComponent> = TestBed.createComponent(ManageSubscriptionComponent);
    spyOn(TestBed.inject(Router), 'navigate').and.returnValue(Promise.resolve(true));
    fixture.detectChanges();
    tick();
    expect(subscriptionMock.getPlans).not.toHaveBeenCalled();
  }));

  // ── isCancelScheduled guard ────────────────────────────────────────────────

  it('isCancelScheduled is true when status is Active and cancelAtPeriodEnd is true', () => {
    subscriptionMock.getCurrent.and.returnValue(of(makeSub()));
    const component = TestBed.createComponent(ManageSubscriptionComponent).componentInstance;
    component.subscription.set(makeSub({ status: 'Active', cancelAtPeriodEnd: true }));
    expect(component.isCancelScheduled).toBeTrue();
  });

  it('isCancelScheduled is false when status is Canceled even if cancelAtPeriodEnd is true', () => {
    subscriptionMock.getCurrent.and.returnValue(of(makeSub()));
    const component = TestBed.createComponent(ManageSubscriptionComponent).componentInstance;
    component.subscription.set(makeSub({ status: 'Canceled', cancelAtPeriodEnd: true }));
    expect(component.isCancelScheduled).toBeFalse();
  });
});
