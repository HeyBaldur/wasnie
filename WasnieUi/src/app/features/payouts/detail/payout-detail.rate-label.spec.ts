import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, provideRouter } from '@angular/router';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TranslateModule, TranslateService } from '@ngx-translate/core';
import { PayoutDetailComponent } from './payout-detail.component';
import { PayoutDetail, RateTableDto } from '../models/payout.model';

/**
 * The line under each rule name on a payout's breakdown.
 *
 * ★★ THE REPORTED BUG. A payout that had already been PAID showed
 * `0–2000000% @ 4% / 2000000–5000000% @ 6%`. Its stored attainment bounds are 0 / 20000 / 50000 —
 * money typed into a ladder whose bounds are ratios of quota — and this label multiplied them by 100
 * and appended "%". Two lies compounding: the table is malformed AND the screen described it in a
 * unit it was never in.
 *
 * ★ NOTHING HERE TOUCHES A FIGURE. The commission, the base amount and the credit are the server's
 * and are unchanged; this is the sentence beside them.
 */
describe('Payout breakdown — every value with its own unit', () => {
  let component: PayoutDetailComponent;

  const payout = (currency: string): PayoutDetail =>
    ({ id: 'p1', totalCommissionCurrency: currency, lines: [] }) as unknown as PayoutDetail;

  function setup(currency = 'EUR', lang = 'en-US'): PayoutDetailComponent {
    TestBed.configureTestingModule({
      imports: [PayoutDetailComponent, TranslateModule.forRoot()],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { paramMap: { get: () => 'payout-1' } } },
        },
      ],
    });
    TestBed.overrideComponent(PayoutDetailComponent, { set: { template: '' } });

    const translate = TestBed.inject(TranslateService);
    translate.setTranslation(lang, {
      PLANS: { ATT_BOUND_SUFFIX: '× quota', RATE_PER_UNIT_SUFFIX: 'per unit' },
    });
    translate.use(lang);

    const c = TestBed.createComponent(PayoutDetailComponent).componentInstance;
    c.payout.set(payout(currency));
    return c;
  }

  afterEach(() => TestBed.resetTestingModule());

  const rt = (over: Partial<RateTableDto>): RateTableDto =>
    ({ type: 'Flat', flatRate: null, tiers: null, attainmentTiers: null,
       measurementBase: 'TransactionAmount', ...over }) as RateTableDto;

  // ── Semantic 1: a rate ────────────────────────────────────────────────────────────────────

  it('writes a flat rate on the transaction amount as a percentage', () => {
    component = setup();
    expect(component.rateLabel(rt({ type: 'Flat', flatRate: 0.05 }))).toBe('5% flat');
  });

  // ── Semantic 2: an amount per unit ────────────────────────────────────────────────────────

  it('writes a flat rate on the quantity as money per unit, never as a percentage', () => {
    component = setup();
    const label = component.rateLabel(
      rt({ type: 'Flat', flatRate: 5, measurementBase: 'TransactionQuantity' }));

    expect(label).toContain('per unit');
    expect(label).not.toContain('%');
  });

  // ── Semantic 3: a Tiered bound is money ───────────────────────────────────────────────────

  it('writes Tiered bounds in the plan currency', () => {
    component = setup('EUR');
    const label = component.rateLabel(rt({
      type: 'Tiered',
      tiers: [{ from: 0, to: 1000, rate: 0.05 }, { from: 1000, to: null, rate: 0.09 }],
    }));

    expect(label).toBe('€0.00–€1,000.00 @ 5% / €1,000.00+ @ 9%');
  });

  it('follows the payout currency rather than assuming euros', () => {
    component = setup('USD');
    expect(component.rateLabel(rt({
      type: 'Tiered', tiers: [{ from: 0, to: 1000, rate: 0.05 }],
    }))).toContain('$');
  });

  // ── Semantic 4: an attainment bound is a proportion of quota ──────────────────────────────

  it('writes attainment bounds as ratios of quota, the way the rule form does', () => {
    component = setup();
    const label = component.rateLabel(rt({
      type: 'AttainmentBased',
      attainmentTiers: [
        { attainmentFrom: 0, attainmentTo: 1, rate: 0.04 },
        { attainmentFrom: 1, attainmentTo: null, rate: 0.07 },
      ],
    }));

    expect(label).toBe('0–1 × quota @ 4% / 1+ × quota @ 7%');
  });

  /**
   * ★★ THE HISTORICAL PAYOUT, PINNED. The table stored against payout 844E7E75 is malformed — its
   * bounds are currency amounts sitting in a ratio ladder — and it stays malformed: this is a
   * presentation change and not one stored figure moves. What it must never do again is call those
   * bounds percentages.
   */
  it('shows the malformed historical table as absurd ratios, not as absurd percentages', () => {
    component = setup();
    const label = component.rateLabel(rt({
      type: 'AttainmentBased',
      attainmentTiers: [
        { attainmentFrom: 0, attainmentTo: 20000, rate: 0.04 },
        { attainmentFrom: 20000, attainmentTo: 50000, rate: 0.06 },
      ],
    }));

    expect(label).toBe('0–20,000 × quota @ 4% / 20,000–50,000 × quota @ 6%');

    // The string that was on screen is now unreachable.
    expect(label).not.toContain('2000000');
    expect(label).not.toContain('2,000,000');
  });

  // ── The fallback ──────────────────────────────────────────────────────────────────────────

  /**
   * ★ NO VALUES, NO INVENTED UNIT. An empty or unrecognised table falls back to naming its type. It
   * is less informative than a ladder and it is not wrong, which is the trade this WI asks for.
   */
  it('names the table type when there is nothing to describe', () => {
    component = setup();
    expect(component.rateLabel(rt({ type: 'AttainmentBased', attainmentTiers: [] })))
      .toBe('AttainmentBased');
    expect(component.rateLabel(rt({ type: 'Tiered', tiers: [] }))).toBe('Tiered');
  });
});
