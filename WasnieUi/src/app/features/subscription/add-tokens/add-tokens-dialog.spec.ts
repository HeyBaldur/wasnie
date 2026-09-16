/**
 * KAN-83 UX — buying additional AI tokens from the dialog.
 *
 * ★ THE FIXTURE IS THE SHAPE `GET /api/subscription/boosts` SENDS (§A4): `amountCents` as an integer in the smallest
 * unit, `currency` lowercase, exactly as Stripe reports them.
 */
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { provideRouter } from '@angular/router';
import { TranslateModule } from '@ngx-translate/core';
import { of, throwError } from 'rxjs';

import { AddTokensDialogComponent, tokenPackViews } from './add-tokens-dialog.component';
import { checkoutNavigator } from '../plan-offer/start-checkout';
import { BillingDetails, BoostOffer, SubscriptionService } from '../services/subscription.service';

const OFFERS: BoostOffer[] = [
  { priceId: 'price_3m', tokens: 3_000_000, amountCents: 2000, currency: 'eur' },
  { priceId: 'price_6m', tokens: 6_000_000, amountCents: 3000, currency: 'eur' },
  { priceId: 'price_12m', tokens: 12_000_000, amountCents: 4000, currency: 'eur' },
];

const DETAILS: BillingDetails = {
  subscription: null,
  paymentMethod: { type: 'card', brand: 'visa', last4: '4242', expMonth: 1, expYear: 2027 },
  invoices: [],
  synced: false,
};

describe('tokenPackViews', () => {
  it('works out the price per million, which is how the packs actually compare', () => {
    const [three, six, twelve] = tokenPackViews(OFFERS, 'en');

    expect(three.perMillion).toBeCloseTo(6.667, 3);
    expect(six.perMillion).toBeCloseTo(5, 3);
    expect(twelve.perMillion).toBeCloseTo(3.333, 3);
  });

  it('★ marks the best value from the prices themselves, never from a hand-set flag', () => {
    expect(tokenPackViews(OFFERS, 'en').filter(v => v.bestValue).map(v => v.priceId)).toEqual(['price_12m']);
  });

  it('★ the label follows a repricing with no code change', () => {
    const cheapened = [{ ...OFFERS[0], amountCents: 300 }, ...OFFERS.slice(1)];

    expect(tokenPackViews(cheapened, 'en').filter(v => v.bestValue).map(v => v.priceId)).toEqual(['price_3m']);
  });

  it('★ a single pack is not the best value of anything', () => {
    expect(tokenPackViews([OFFERS[0]], 'en')[0].bestValue).toBeFalse();
  });

  it('a pack granting no tokens does not divide by zero', () => {
    expect(tokenPackViews([{ priceId: 'p', tokens: 0, amountCents: 100, currency: 'eur' }], 'en')[0].perMillion).toBe(0);
  });
});

describe('AddTokensDialogComponent', () => {
  let fixture: ComponentFixture<AddTokensDialogComponent>;
  let service: jasmine.SpyObj<SubscriptionService>;

  const el = () => fixture.nativeElement as HTMLElement;
  const text = () => el().textContent ?? '';

  function build(offers: BoostOffer[] = OFFERS, details: BillingDetails = DETAILS): void {
    service = jasmine.createSpyObj<SubscriptionService>('SubscriptionService',
      ['getBoostOffers', 'getBillingDetails', 'createBoostCheckout']);
    service.getBoostOffers.and.returnValue(of(offers));
    service.getBillingDetails.and.returnValue(of(details));

    TestBed.configureTestingModule({
      imports: [AddTokensDialogComponent, TranslateModule.forRoot()],
      providers: [provideRouter([]), { provide: SubscriptionService, useValue: service }],
    });

    fixture = TestBed.createComponent(AddTokensDialogComponent);
  }

  function open(): void {
    fixture.componentRef.setInput('open', true);
    fixture.detectChanges();
  }

  beforeEach(() => {
    TestBed.resetTestingModule();
    build();
  });

  it('★ asks Stripe for the packs only once it is opened', () => {
    fixture.detectChanges();
    expect(service.getBoostOffers).not.toHaveBeenCalled();

    open();
    expect(service.getBoostOffers).toHaveBeenCalledTimes(1);
  });

  it('lists the packs with their size and price per million', () => {
    open();

    expect(el().querySelectorAll('.add-tokens__pack').length).toBe(3);
    expect(text()).toContain('ASSISTANT_USAGE.ADDON_PER_MILLION');
  });

  it('★★ choosing a pack does NOT start a payment — it asks for confirmation first', () => {
    open();
    fixture.componentInstance.choose(fixture.componentInstance.packs()[0]);
    fixture.detectChanges();

    // A mis-click on a chat alert must not be able to charge a card.
    expect(service.createBoostCheckout).not.toHaveBeenCalled();
    expect(el().querySelector('[data-testid="add-tokens-confirm"]')).not.toBeNull();
    expect(text()).toContain('ASSISTANT_USAGE.CONFIRM_LINE');
  });

  it('names the card the charge will go to', () => {
    open();
    fixture.componentInstance.choose(fixture.componentInstance.packs()[0]);
    fixture.detectChanges();

    expect(text()).toContain('ASSISTANT_USAGE.CONFIRM_CARD');
  });

  it('★ with no card on file it says one will be asked for, instead of naming a card that is not there', () => {
    TestBed.resetTestingModule();
    build(OFFERS, { ...DETAILS, paymentMethod: null });
    open();
    fixture.componentInstance.choose(fixture.componentInstance.packs()[0]);
    fixture.detectChanges();

    expect(text()).toContain('ASSISTANT_USAGE.CONFIRM_NO_CARD');
    expect(text()).not.toContain('ASSISTANT_USAGE.CONFIRM_CARD');
  });

  it('confirming checks out that pack and carries where to return to', () => {
    service.createBoostCheckout.and.returnValue(of({ checkoutUrl: 'https://checkout.stripe.test/cs_1' }));
    const go = spyOn(checkoutNavigator, 'go');
    fixture.componentRef.setInput('returnTo', 'Assistant');
    open();

    fixture.componentInstance.choose(fixture.componentInstance.packs()[1]);
    fixture.componentInstance.confirm();

    expect(service.createBoostCheckout).toHaveBeenCalledOnceWith('price_6m', 'Assistant');
    expect(go).toHaveBeenCalledOnceWith('https://checkout.stripe.test/cs_1');
  });

  it('★ a double-click cannot start two checkouts', () => {
    service.createBoostCheckout.and.returnValue(of({ checkoutUrl: 'https://checkout.stripe.test/cs_1' }));
    spyOn(checkoutNavigator, 'go');
    open();

    fixture.componentInstance.choose(fixture.componentInstance.packs()[0]);
    fixture.componentInstance.confirm();
    fixture.componentInstance.confirm();

    expect(service.createBoostCheckout).toHaveBeenCalledTimes(1);
  });

  it('going back from the confirmation charges nothing and returns to the list', () => {
    open();
    fixture.componentInstance.choose(fixture.componentInstance.packs()[0]);
    fixture.componentInstance.back();
    fixture.detectChanges();

    expect(service.createBoostCheckout).not.toHaveBeenCalled();
    expect(el().querySelectorAll('.add-tokens__pack').length).toBe(3);
  });

  it('a failed checkout says so and lets the customer try again', () => {
    service.createBoostCheckout.and.returnValue(throwError(() => new HttpErrorResponse({ status: 400 })));
    open();

    fixture.componentInstance.choose(fixture.componentInstance.packs()[0]);
    fixture.componentInstance.confirm();
    fixture.detectChanges();

    expect(el().querySelector('[data-testid="add-tokens-error"]')).not.toBeNull();
    expect(fixture.componentInstance.submitting()).toBeFalse();
  });

  it('★ no packs on sale is stated, not shown as an empty list', () => {
    TestBed.resetTestingModule();
    build([]);
    open();

    expect(el().querySelector('[data-testid="add-tokens-unavailable"]')).not.toBeNull();
  });

  it('★ closing resets the flow, so it never reopens on a stale confirmation step', () => {
    open();
    fixture.componentInstance.choose(fixture.componentInstance.packs()[0]);
    fixture.componentInstance.close();
    fixture.detectChanges();

    expect(fixture.componentInstance.selected()).toBeNull();
  });

  it('★ no visible copy in the dialog says "boost"', () => {
    open();

    expect(text().toLowerCase()).not.toContain('boost');
  });
});
