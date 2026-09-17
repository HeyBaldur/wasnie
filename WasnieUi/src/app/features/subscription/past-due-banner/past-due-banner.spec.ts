/**
 * The payment-overdue banner.
 *
 * ★★ THE POINT OF THESE TESTS IS THE DATE. The banner used to promise that access ends "once the grace period
 * ends" — a period this system does not define, measure or expose, so the deadline could never be shown. It
 * now states the one real fact available, the date Stripe will retry the card, and says nothing about a
 * deadline when Stripe has not scheduled one. Both halves are fixed here.
 */
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { TranslateModule } from '@ngx-translate/core';
import { signal } from '@angular/core';
import { Subject, of, throwError } from 'rxjs';
import { HttpErrorResponse } from '@angular/common/http';
import { provideRouter } from '@angular/router';

import { PastDueBannerComponent } from './past-due-banner.component';
import { SubscriptionStateService } from '../services/subscription-state.service';
import { BillingDetails, BillingInvoice, SubscriptionService } from '../services/subscription.service';
import { WsToastService } from '../../../shared/ui/ws-toast/ws-toast.service';

const invoice = (overrides: Partial<BillingInvoice> = {}): BillingInvoice => ({
  id: 'in_1', number: 'A-1', description: 'Incentra Pro', createdAt: '2026-09-14T00:00:00Z',
  total: 299, currency: 'eur', status: 'open', hostedInvoiceUrl: null, invoicePdfUrl: null,
  nextPaymentAttempt: null, attemptCount: 1, ...overrides,
});

const details = (invoices: BillingInvoice[]): BillingDetails => ({
  subscription: null, paymentMethod: null, invoices, synced: false,
});

describe('PastDueBannerComponent', () => {
  let fixture: ComponentFixture<PastDueBannerComponent>;
  let service: jasmine.SpyObj<SubscriptionService>;
  let toast: jasmine.SpyObj<WsToastService>;

  const el = () => fixture.nativeElement as HTMLElement;
  const text = () => el().textContent ?? '';

  function build(pastDue: boolean, invoices: BillingInvoice[] = []): void {
    TestBed.resetTestingModule();

    service = jasmine.createSpyObj<SubscriptionService>('SubscriptionService',
      ['getBillingDetails', 'getBillingPortalUrl']);
    service.getBillingDetails.and.returnValue(of(details(invoices)));
    toast = jasmine.createSpyObj<WsToastService>('WsToastService', ['show']);

    TestBed.configureTestingModule({
      imports: [PastDueBannerComponent, TranslateModule.forRoot()],
      providers: [
        // ws-button supports routerLink, so it asks for ActivatedRoute even when no link is given.
        provideRouter([]),
        { provide: SubscriptionService, useValue: service },
        { provide: WsToastService, useValue: toast },
        { provide: SubscriptionStateService, useValue: { isPastDue: signal(pastDue) } },
      ],
    });

    fixture = TestBed.createComponent(PastDueBannerComponent);
    fixture.detectChanges();
  }

  it('shows nothing while the account is in good standing', () => {
    build(false);

    expect(text().trim()).toBe('');
  });

  it('★ does not even ask Stripe unless the account is overdue', () => {
    // This component renders on every page; a live billing read on all of them would put a third-party call
    // on the critical path of the whole app for a state most accounts are never in.
    build(false);

    expect(service.getBillingDetails).not.toHaveBeenCalled();
  });

  it('★★ names the date Stripe will retry, instead of an unnamed grace period', () => {
    build(true, [invoice({ nextPaymentAttempt: '2026-09-23T10:00:00Z' })]);

    expect(fixture.componentInstance.retryDate()).toBe('2026-09-23T10:00:00Z');
    expect(text()).toContain('SUBSCRIPTION.PAST_DUE_BANNER_RETRY');
    expect(text()).not.toContain('SUBSCRIPTION.PAST_DUE_BANNER_MSG');
  });

  it('★★ says nothing about a deadline when Stripe has not scheduled one', () => {
    // The old copy promised one regardless. Inventing a date here would be worse than the vagueness it replaced.
    build(true, [invoice({ nextPaymentAttempt: null })]);

    expect(fixture.componentInstance.retryDate()).toBeNull();
    expect(text()).toContain('SUBSCRIPTION.PAST_DUE_BANNER_MSG');
    expect(text()).not.toContain('SUBSCRIPTION.PAST_DUE_BANNER_RETRY');
  });

  it('★ ignores a settled invoice — only the unpaid one carries a retry', () => {
    build(true, [
      invoice({ id: 'in_paid', status: 'paid', nextPaymentAttempt: '2026-09-20T10:00:00Z' }),
      invoice({ id: 'in_open', status: 'open', nextPaymentAttempt: '2026-09-23T10:00:00Z' }),
    ]);

    expect(fixture.componentInstance.retryDate()).toBe('2026-09-23T10:00:00Z');
  });

  it('★ Stripe being unreachable loses the date, not the warning', () => {
    TestBed.resetTestingModule();
    service = jasmine.createSpyObj<SubscriptionService>('SubscriptionService',
      ['getBillingDetails', 'getBillingPortalUrl']);
    service.getBillingDetails.and.returnValue(throwError(() => new HttpErrorResponse({ status: 500 })));

    TestBed.configureTestingModule({
      imports: [PastDueBannerComponent, TranslateModule.forRoot()],
      providers: [
        provideRouter([]),
        { provide: SubscriptionService, useValue: service },
        { provide: WsToastService, useValue: jasmine.createSpyObj<WsToastService>('WsToastService', ['show']) },
        { provide: SubscriptionStateService, useValue: { isPastDue: signal(true) } },
      ],
    });
    fixture = TestBed.createComponent(PastDueBannerComponent);
    fixture.detectChanges();

    expect(text()).toContain('SUBSCRIPTION.PAST_DUE_BANNER_TITLE');
    expect(fixture.componentInstance.retryDate()).toBeNull();
  });

  it('★ the action opens the billing portal, and a double click does not open two', () => {
    build(true, [invoice()]);
    // ★ A PENDING REQUEST, NOT `of(...)`. A synchronous observable resolves before the second click, which is
    // the one case the guard is NOT for — over real HTTP the call is always in flight when the impatient
    // second click lands. A subject reproduces that; `of` would have tested nothing and passed.
    const pending = new Subject<{ url: string }>();
    service.getBillingPortalUrl.and.returnValue(pending.asObservable());
    const open = spyOn(window, 'open');

    fixture.componentInstance.openBillingPortal();
    fixture.componentInstance.openBillingPortal();

    expect(service.getBillingPortalUrl).toHaveBeenCalledTimes(1);

    pending.next({ url: 'https://billing.stripe.test/session' });
    expect(open).toHaveBeenCalledOnceWith('https://billing.stripe.test/session', '_blank');
    expect(fixture.componentInstance.opening()).toBeFalse();
  });

  it('a failed portal call clears the pending state and says so', () => {
    build(true, [invoice()]);
    service.getBillingPortalUrl.and.returnValue(throwError(() => new HttpErrorResponse({ status: 500 })));

    fixture.componentInstance.openBillingPortal();

    expect(fixture.componentInstance.opening()).toBeFalse();
    expect(toast.show).toHaveBeenCalledWith('SUBSCRIPTION.BILLING_PORTAL_ERROR', 'error');
  });
});
