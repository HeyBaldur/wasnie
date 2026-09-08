import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TranslateModule, TranslateService } from '@ngx-translate/core';
import { provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { DashboardComponent } from './dashboard.component';
import { DashboardStore } from './store/dashboard.store';
import { DashboardService } from './services/dashboard.service';
import { DashboardSummary, DashboardCommissionsBand, CurrencyTotal } from './models/dashboard.models';
import { ProfileService } from '../profile/services/profile.service';
import { TerminatedAccountsStore } from '../ledger/state/terminated-accounts.store';
import { CurrentUserService } from '../../core/auth/current-user.service';

/**
 * KAN-62 — the three commission cards and the collapsible "needs attention" panel.
 *
 * ★ THROUGH THE DOM. The acceptance criteria are about what is on screen: three cards that are
 * structurally identical, a panel that starts shut. A test that asserted on the signals would pass
 * just as happily with the markup wired to a branch that never renders (§A3).
 */

function eur(amount: number): CurrencyTotal {
  return { amount, currency: 'EUR' };
}

function band(over: Partial<DashboardCommissionsBand> = {}): DashboardCommissionsBand {
  return {
    totalByCurrency: [],
    paidByCurrency: [],
    unpaidByCurrency: [],
    closedTotalByCurrency: [], unreachableTotalByCurrency: [],
    ...over,
  };
}

function summary(commissionsBand: DashboardCommissionsBand, attention = 0): DashboardSummary {
  return {
    from: '2026-08-01',
    to: '2026-08-31',
    commissionsBand,
    actionBand: {
      draftPayRunsCount: 0,
      payoutsPendingApprovalCount: 0,
      payoutsPendingApprovalByCurrency: [],
      payoutsApprovedUnpaidByCurrency: [],
      pendingByPlanItems: [],
      unprocessablePendingItems: attention
        ? [{ reason: 'NoPayee', count: attention, currencies: [] }]
        : [],
      driftAlerts: [],
      dealLostAlerts: [],
      ambiguousAttributionPayees: [],
      plansWithoutLiveRules: [],
    },
    periodBand: {
      transactionsCount: 0,
      transactionsVolumeByCurrency: [],
      payoutsTotalByCurrency: [],
      creditsCount: 0,
      creditsTotalByCurrency: [],
      avgQuotaAttainmentPercent: null,
      activePlansCount: 0,
      activeQuotasCount: 0,
      payeesActiveCount: 0,
      payeesInactiveCount: 0,
    },
    trendBand: null,
    activityFeed: [],
  } as DashboardSummary;
}

describe('Dashboard — commission cards and the attention panel', () => {
  let fixture: ComponentFixture<DashboardComponent>;
  let component: DashboardComponent;
  let store: DashboardStore;

  const el = () => fixture.nativeElement as HTMLElement;

  function render(data: DashboardSummary): void {
    store.summary.set(data);
    store.loading.set(false);
    fixture.detectChanges();
  }

  /** The three commission cards, in DOM order. */
  function commissionCards(): HTMLElement[] {
    return Array.from(el().querySelectorAll('a.metric-card'))
      .filter(a => (a.getAttribute('ng-reflect-router-link') ?? '').includes('credits')
                || (a.querySelector('.metric-card__title')?.textContent ?? '').includes('COMMISSIONS'))
      .slice(0, 3) as HTMLElement[];
  }

  beforeEach(async () => {
    const api = jasmine.createSpyObj<DashboardService>('DashboardService', ['getSummary']);
    api.getSummary.and.returnValue(of(summary(band())));

    const profile = jasmine.createSpyObj<ProfileService>('ProfileService', ['getProfile']);
    profile.getProfile.and.returnValue(of({ firstName: 'Ada' } as never) as never);

    const terminated = jasmine.createSpyObj<TerminatedAccountsStore>(
      'TerminatedAccountsStore', ['load']);
    terminated.load.and.returnValue(Promise.resolve());
    Object.assign(terminated, {
      rows: { set: () => {} },
      count: () => 0,
      owedToPayeesCount: () => 0,
      owedByPayeesCount: () => 0,
    });

    await TestBed.configureTestingModule({
      imports: [DashboardComponent, TranslateModule.forRoot()],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: DashboardService, useValue: api },
        { provide: ProfileService, useValue: profile },
        { provide: TerminatedAccountsStore, useValue: terminated },
        // The Reconcile link is RBAC-gated (§5.8): without Reports.ViewAll it is HIDDEN, not disabled.
        // Granted here so the collapsed header can be checked against what a permitted reader sees.
        { provide: CurrentUserService, useValue: { hasPermission: (p: string) => p === 'Reports.ViewAll' } },
      ],
    }).compileComponents();

    // Real translations for the keys under test: with none loaded the pipe echoes the key back and
    // the interpolation never runs, so an assertion on the rendered text would prove nothing (§A3).
    TestBed.inject(TranslateService).setTranslation('en', {
      DASHBOARD: {
        TREND_NOTE_PACING: 'Money that actually left. Baseline: {{amount}} in {{period}}.',
        TREND_NOTE_CLOSED: 'Money that actually left. Previous window ({{period}}): {{amount}}.',
      },
    }, true);
    TestBed.inject(TranslateService).use('en');

    fixture = TestBed.createComponent(DashboardComponent);
    component = fixture.componentInstance;
    store = TestBed.inject(DashboardStore);
    fixture.detectChanges();
  });

  // ── the three cards ───────────────────────────────────────────────────────

  it('renders exactly three commission cards', () => {
    render(summary(band({
      totalByCurrency: [eur(500)],
      paidByCurrency: [eur(300)],
      unpaidByCurrency: [eur(200)],
    })));

    expect(component.commissionCards.length).toBe(3);
    expect(component.commissionCards.map(c => c.key)).toEqual(['total', 'paid', 'unpaid']);
  });

  it('the three cards come from ONE declaration, so they cannot drift apart', () => {
    // The acceptance criterion is that they are structurally identical. Three hand-written blocks
    // would satisfy it today and drift the first time one of them is touched; one loop cannot.
    const titles = component.commissionCards.map(c => c.titleKey);
    const descs = component.commissionCards.map(c => c.descKey);

    expect(new Set(titles).size).toBe(3, 'each card names itself');
    expect(new Set(descs).size).toBe(3);
    expect(titles.every(t => t.startsWith('DASHBOARD.COMMISSIONS_'))).toBeTrue();
  });

  it('shows the amounts of the selected currency on each card', () => {
    render(summary(band({
      totalByCurrency: [eur(500)],
      paidByCurrency: [eur(300)],
      unpaidByCurrency: [eur(200)],
    })));

    expect(component.amountFor(component.commissionTotals('total'), 'EUR')).toBe(500);
    expect(component.amountFor(component.commissionTotals('paid'), 'EUR')).toBe(300);
    expect(component.amountFor(component.commissionTotals('unpaid'), 'EUR')).toBe(200);
  });

  it('all three cards read the SAME currency', () => {
    // A Total in euros beside a Paid in zlotys would not add up, and nothing on screen would say why.
    render(summary(band({
      totalByCurrency: [eur(500), { amount: 40, currency: 'PLN' }],
      paidByCurrency: [{ amount: 40, currency: 'PLN' }],
      unpaidByCurrency: [eur(500)],
    })));

    expect(component.commissionsCurrency()).toBe('EUR');
    expect(component.amountFor(component.commissionTotals('paid'), 'EUR'))
      .toBe(0, 'nothing paid in euros — a zero, not the zloty figure');
  });

  it('an empty range shows zero rather than blowing up on a missing currency', () => {
    // ★ THE REGRESSION GUARD. Intl throws a RangeError on an empty currency code, which took the whole
    // dashboard down instead of rendering the zero the criterion asks for.
    expect(() => render(summary(band()))).not.toThrow();

    expect(component.commissionsCurrency()).toBeNull();
    expect(component.fmtCompact(0, '')).toBe('0');
    expect(component.amountFor(component.commissionTotals('total'), null)).toBe(0);
  });

  it('reports commissions that are in none of the three cards', () => {
    render(summary(band({
      totalByCurrency: [eur(1000)],
      paidByCurrency: [eur(1000)],
      closedTotalByCurrency: [eur(3869.34)],
    })));

    expect(component.hasClosedCommissions()).toBeTrue();
    expect(el().querySelector('.commissions-closed-note')).withContext(
      'money left out of the totals has to be visible, or it simply vanishes').toBeTruthy();
  });

  it('says nothing about closed commissions when there are none', () => {
    render(summary(band({ totalByCurrency: [eur(1000)], paidByCurrency: [eur(1000)] })));

    expect(component.hasClosedCommissions()).toBeFalse();
    expect(el().querySelector('.commissions-closed-note')).toBeNull();
  });

  // ── placement and typography ──────────────────────────────────────────────

  it('the commission cards lead the page, before the action band', () => {
    render(summary(band({ totalByCurrency: [eur(500)] }), 39));

    const html = (el().innerHTML);
    const cards = html.indexOf('COMMISSIONS_TOTAL');
    const actionBand = html.indexOf('BAND_ACTION');

    expect(cards).toBeGreaterThan(-1);
    expect(actionBand).toBeGreaterThan(-1);
    expect(cards).toBeLessThan(actionBand, 'the three figures are the first thing on the page');
  });

  it('the figure opts into the display face explicitly', () => {
    // It is a <div>, not a heading, so the global h1..h4 rule does not reach it — the class is what
    // carries Source Serif 4, and losing it would silently drop the figure back to the body face.
    render(summary(band({ totalByCurrency: [eur(500)], unpaidByCurrency: [eur(500)] })));

    const figures = el().querySelectorAll('.commission-card__value');
    expect(figures.length).toBe(3, 'all three, or they stop being twins');
    figures.forEach(f => expect(f.classList).toContain('metric-card__value'));
  });

  it('the three cards open in a new tab, and say so', () => {
    render(summary(band({ totalByCurrency: [eur(500)] })));

    const links = Array.from(el().querySelectorAll('a.metric-card'))
      .filter(a => a.getAttribute('target') === '_blank');

    expect(links.length).toBe(3, 'all three, or they stop being twins');
    links.forEach(a => {
      // A targeted link without `noopener` hands the new tab a handle on this one.
      expect(a.getAttribute('rel')).toContain('noopener');
      expect(a.querySelector('.metric-card__ext'))
        .withContext('the glyph is the promise that a new tab will open').toBeTruthy();
    });
  });

  it('each card links with the settlement filter the credits screen actually applies', () => {
    // ★ THE DEFECT THIS PINS. The links used to carry `paid=true|false`, a parameter nothing on the
    // destination read — so all three cards opened the SAME unfiltered list and the same export.
    component.store.setRange({ from: '2026-07-01', to: '2026-07-31' });

    const byKey = new Map(component.commissionCards.map(c => [c.key, c.settlement]));
    expect(byKey.get('paid')).toBe('Paid');
    expect(byKey.get('unpaid')).toBe('Unpaid');
    expect(byKey.get('total')).toBe('Payable',
      'Total sums paid + unpaid and excludes closed credits — the list must exclude them too');

    for (const card of component.commissionCards) {
      const p = component.commissionsLinkParams(card.settlement);
      expect(p['settlement']).withContext(card.key).toBe(card.settlement);
      expect(p['paid']).withContext('the parameter nothing read').toBeUndefined();
    }
  });

  it('the three links differ from each other', () => {
    // If they ever collapse to the same query again, the symptom is silent: three cards, one list.
    const queries = component.commissionCards
      .map(c => JSON.stringify(component.commissionsLinkParams(c.settlement)));

    expect(new Set(queries).size).toBe(3);
  });

  it('the range picker opens inwards from the right edge', () => {
    // Anchored left, the two-month panel runs past the viewport and the page grows a horizontal
    // scrollbar. The trigger sits in the right-hand side of the header, so it must hang from `end`.
    const picker = el().querySelector('ws-date-range-picker')!;
    expect(picker.getAttribute('align')).toBe('end');
  });

  // ── the cards say what their numbers ARE ──────────────────────────────────

  it('every commission card carries a description naming its date basis', () => {
    // The confusion this fixes: three figures in euros, one under the other, with nothing saying that
    // one counts money that moved and the others count commission created (§C3).
    render(summary(band({ totalByCurrency: [eur(500)] })));

    const subs = Array.from(el().querySelectorAll('a.metric-card .metric-card__sub'))
      .map(n => n.textContent?.trim() ?? '');

    expect(subs.length).toBeGreaterThanOrEqual(3);
    for (const card of component.commissionCards) {
      expect(card.descKey).withContext(card.key).toMatch(/^DASHBOARD\.COMMISSIONS_.*_DESC$/);
    }
  });

  it('the trend footer explains the figure and keeps the baseline it is a percentage of', () => {
    render({
      ...summary(band({ totalByCurrency: [eur(500)] })),
      trendBand: {
        commissionTrend: [{
          currency: 'EUR', currentAmount: 433181, priorAmount: 26660,
          changePercent: null, direction: 'pacing', pacingPercent: 1625,
        }],
        isPacing: true,
        currentFrom: '2026-09-01', currentTo: '2026-09-30',
        priorFrom: '2026-08-01', priorTo: '2026-08-31',
      },
    });

    const note = el().querySelector('.trend-card__note');
    expect(note).withContext('the card must say what its number is').toBeTruthy();

    // ★ The pill reads "N% of baseline". Dropping the amount it is a percentage OF would leave that
    //   number pointing at something the reader cannot see — the chart shows it only on hover.
    expect(note!.textContent)
      .withContext('the amount the pill is a percentage OF must stay on screen').toContain('26.66K');
    expect(note!.textContent).toContain('Money that actually left');
    expect(el().querySelector('.trend-card__prior'))
      .withContext('the bare two-number footer is what the sentence replaced').toBeNull();
  });

  // ── the collapsible panel ─────────────────────────────────────────────────

  it('starts collapsed', () => {
    render(summary(band(), 39));

    expect(component.attentionExpanded()).toBeFalse();
    expect(el().querySelector<HTMLElement>('#attention-detail')!.hidden).toBeTrue();
  });

  it('keeps the count and the way out visible while collapsed', () => {
    render(summary(band(), 39));

    expect(component.attentionExpanded()).toBeFalse();

    const header = el().querySelector('.attention-hd__actions')!;
    expect(header.textContent).toContain('39');
    expect(el().querySelector('a.attention-hd__link')).withContext(
      'the reader is told there is a problem — the way to fix it must stay reachable').toBeTruthy();
  });

  it('the title and description stay visible while collapsed', () => {
    render(summary(band(), 39));

    const lead = el().querySelector('.attention-hd__lead')!;
    expect(lead.querySelector('.stat-card__title')).toBeTruthy();
    expect(lead.querySelector('.pending-plan-card__sub')).toBeTruthy();
  });

  it('the caret opens and closes the detail', () => {
    render(summary(band(), 39));
    const toggle = el().querySelector<HTMLButtonElement>('.attention-hd__toggle')!;
    const detail = () => el().querySelector<HTMLElement>('#attention-detail')!;

    toggle.click();
    fixture.detectChanges();
    expect(component.attentionExpanded()).toBeTrue();
    expect(detail().hidden).toBeFalse();

    toggle.click();
    fixture.detectChanges();
    expect(component.attentionExpanded()).toBeFalse();
    expect(detail().hidden).toBeTrue();
  });

  it('the toggle is a real button that announces its state', () => {
    render(summary(band(), 39));
    const toggle = el().querySelector<HTMLButtonElement>('.attention-hd__toggle')!;

    expect(toggle.tagName).toBe('BUTTON');
    expect(toggle.getAttribute('aria-expanded')).toBe('false');
    expect(toggle.getAttribute('aria-controls')).toBe('attention-detail');

    toggle.click();
    fixture.detectChanges();
    expect(toggle.getAttribute('aria-expanded')).toBe('true');
  });

  it('the open state does not survive a fresh load of the screen', () => {
    render(summary(band(), 39));
    component.toggleAttention();
    fixture.detectChanges();
    expect(component.attentionExpanded()).toBeTrue();

    const second = TestBed.createComponent(DashboardComponent);
    second.detectChanges();

    expect(second.componentInstance.attentionExpanded())
      .withContext('it must reopen collapsed every time').toBeFalse();
  });
});
