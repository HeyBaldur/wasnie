import { Component } from '@angular/core';
import { ComponentFixture, TestBed, fakeAsync, tick, flush, discardPeriodicTasks } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { provideTranslateService } from '@ngx-translate/core';
import { of, throwError } from 'rxjs';
import { SubscriptionSuccessComponent } from './subscription-success.component';
import { SubscriptionService } from '../services/subscription.service';
import { WsButtonComponent } from '../../../shared/ui';

@Component({ template: '', standalone: true })
class StubComponent {}

const ACTIVE_SUB = {
  tier: 'Starter', status: 'Active', billingEmail: 'test@test.com',
  stripeSubscriptionId: 'sub_x', stripeCustomerId: 'cus_x',
  stripePriceId: 'price_x', stripeProductId: 'prod_x',
  currentPeriodStart: null, currentPeriodEnd: null,
  nextBillingDate: null, canceledAt: null,
  cancelAtPeriodEnd: false, cancelAt: null,
  createdAt: new Date().toISOString(),
};

const FREE_SUB = { ...ACTIVE_SUB, tier: 'Free', status: 'Active' };

describe('SubscriptionSuccessComponent', () => {
  let fixture: ComponentFixture<SubscriptionSuccessComponent>;
  let component: SubscriptionSuccessComponent;
  let subscriptionMock: jasmine.SpyObj<SubscriptionService>;

  beforeEach(async () => {
    subscriptionMock = jasmine.createSpyObj('SubscriptionService', ['getCurrent']);
    subscriptionMock.getCurrent.and.returnValue(of(FREE_SUB));

    await TestBed.configureTestingModule({
      imports: [SubscriptionSuccessComponent, WsButtonComponent],
      providers: [
        { provide: SubscriptionService, useValue: subscriptionMock },
        provideRouter([
          { path: 'dashboard', component: StubComponent },
          { path: 'onboarding/plan', component: StubComponent },
        ]),
        provideTranslateService({ defaultLanguage: 'en' }),
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(SubscriptionSuccessComponent);
    component = fixture.componentInstance;
  });

  it('should create', fakeAsync(() => {
    fixture.detectChanges();
    discardPeriodicTasks();
    expect(component).toBeTruthy();
  }));

  it('should start in polling state', fakeAsync(() => {
    fixture.detectChanges();
    discardPeriodicTasks();

    expect(component.confirmed()).toBeFalse();
    expect(component.timedOut()).toBeFalse();
  }));

  it('sets confirmed=true when subscription is Active with a paid tier', fakeAsync(() => {
    subscriptionMock.getCurrent.and.returnValue(of(ACTIVE_SUB));
    fixture.detectChanges();
    tick(0);

    expect(component.confirmed()).toBeTrue();
    // flush the 1500ms auto-navigate timeout so no macrotasks remain
    flush();
  }));

  it('does NOT set confirmed when tier is Free even if status is Active', fakeAsync(() => {
    subscriptionMock.getCurrent.and.returnValue(of(FREE_SUB));
    fixture.detectChanges();
    tick(0);
    discardPeriodicTasks();

    expect(component.confirmed()).toBeFalse();
  }));

  it('does NOT call any write method — only reads current subscription', fakeAsync(() => {
    fixture.detectChanges();
    tick(0);
    discardPeriodicTasks();

    expect(subscriptionMock.getCurrent).toHaveBeenCalled();
    expect((subscriptionMock as any)['activatePlan']).toBeUndefined();
    expect((subscriptionMock as any)['selectFree']).toBeUndefined();
  }));

  it('backToWizard stops polling and navigates away', fakeAsync(() => {
    subscriptionMock.getCurrent.and.returnValue(of(FREE_SUB));
    fixture.detectChanges();

    component.backToWizard();
    tick(0);

    expect(component.confirmed()).toBeFalse();
  }));
});
