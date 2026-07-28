import { TestBed, ComponentFixture } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { TranslateModule } from '@ngx-translate/core';
import { DashboardComponent } from './dashboard.component';
import { DashboardStore } from './store/dashboard.store';
import { DashboardSummary, DashboardTrendPoint } from './models/dashboard.models';

// ── DashboardComponent helpers ────────────────────────────────────────────────

describe('DashboardComponent helpers', () => {
  let component: DashboardComponent;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [DashboardComponent, TranslateModule.forRoot()],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    });
    component = TestBed.createComponent(DashboardComponent).componentInstance;
  });

  describe('relativeTime', () => {
    it('returns minutes when less than 60 minutes ago', () => {
      const ts = new Date(Date.now() - 5 * 60 * 1000).toISOString();
      expect(component.relativeTime(ts)).toBe('5m');
    });

    it('returns hours when 2 hours ago', () => {
      const ts = new Date(Date.now() - 2 * 60 * 60 * 1000).toISOString();
      expect(component.relativeTime(ts)).toBe('2h');
    });

    it('returns days when 3 days ago', () => {
      const ts = new Date(Date.now() - 3 * 24 * 60 * 60 * 1000).toISOString();
      expect(component.relativeTime(ts)).toBe('3d');
    });

    it('treats ISO string without Z suffix as UTC (not local time)', () => {
      // C# DateTime serialization omits Z; JS would otherwise add a local-timezone offset
      const tenMinAgo = new Date(Date.now() - 10 * 60 * 1000);
      const noZ = tenMinAgo.toISOString().replace('Z', ''); // strip timezone marker
      expect(component.relativeTime(noZ)).toBe('10m');
    });
  });

  describe('actionCardAccent', () => {
    it('returns "warning" when count > 0', () => {
      expect(component.actionCardAccent(3)).toBe('warning');
    });

    it('returns "none" when count is 0', () => {
      expect(component.actionCardAccent(0)).toBe('none');
    });
  });

  describe('amountsAccent', () => {
    it('returns "warning" when there are currency totals', () => {
      expect(component.amountsAccent([{ amount: 1000, currency: 'EUR' }])).toBe('warning');
    });

    it('returns "none" for empty array', () => {
      expect(component.amountsAccent([])).toBe('none');
    });
  });

  // ── Trend edge-case tests ───────────────────────────────────────────────────

  describe('trendIsNoBase', () => {
    const makePoint = (changePercent: number | null, priorAmount = 100): DashboardTrendPoint => ({
      currency: 'EUR',
      currentAmount: 1000,
      priorAmount,
      changePercent,
      direction: 'up',
    });

    it('returns true when changePercent is null (prior was exactly zero)', () => {
      expect(component.trendIsNoBase(makePoint(null, 0))).toBeTrue();
    });

    it('returns true when changePercent is absurdly large (near-zero prior)', () => {
      // EUR +38,043% case: prior was ~0.01
      expect(component.trendIsNoBase(makePoint(38043, 0.01))).toBeTrue();
    });

    it('returns true when changePercent exceeds 500% threshold', () => {
      expect(component.trendIsNoBase(makePoint(501))).toBeTrue();
    });

    it('returns false for a normal positive change', () => {
      expect(component.trendIsNoBase(makePoint(12.5))).toBeFalse();
    });

    it('returns false for a normal negative change', () => {
      expect(component.trendIsNoBase(makePoint(-45.2))).toBeFalse();
    });

    it('returns false for exactly -100% (full decline)', () => {
      expect(component.trendIsNoBase(makePoint(-100))).toBeFalse();
    });
  });

  describe('trendChangeFormatted', () => {
    it('formats a positive change with + prefix', () => {
      const point: DashboardTrendPoint = {
        currency: 'EUR', currentAmount: 1200, priorAmount: 1000,
        changePercent: 20, direction: 'up',
      };
      expect(component.trendChangeFormatted(point)).toBe('+20.0%');
    });

    it('formats a negative change without + prefix', () => {
      const point: DashboardTrendPoint = {
        currency: 'EUR', currentAmount: 800, priorAmount: 1000,
        changePercent: -20, direction: 'down',
      };
      expect(component.trendChangeFormatted(point)).toBe('-20.0%');
    });

    it('formats zero change without + prefix', () => {
      const point: DashboardTrendPoint = {
        currency: 'EUR', currentAmount: 1000, priorAmount: 1000,
        changePercent: 0, direction: 'neutral',
      };
      expect(component.trendChangeFormatted(point)).toBe('0.0%');
    });
  });

  // ── Activity feed helpers ───────────────────────────────────────────────────

  describe('isSystemActor', () => {
    it('is true for background entries with no actor email (HUBSPOT_TOKEN_REFRESHED)', () => {
      expect(component.isSystemActor('')).toBe(true);
      expect(component.isSystemActor('   ')).toBe(true);
      expect(component.isSystemActor(null)).toBe(true);
    });

    it('is false for a real user', () => {
      expect(component.isSystemActor('admin@domain.com')).toBe(false);
    });
  });

  describe('actorShortName', () => {
    it('returns only the part before @', () => {
      expect(component.actorShortName('admin@domain.com')).toBe('admin');
    });

    it('truncates long usernames to 17 chars + ellipsis', () => {
      const result = component.actorShortName('averylongusername123@example.com');
      expect(result).toBe('averylongusername…');
      expect(result.length).toBeLessThanOrEqual(18);
    });

    it('returns raw string when no @ present', () => {
      expect(component.actorShortName('nodomain')).toBe('nodomain');
    });
  });

  describe('formatActivityAction', () => {
    it('converts underscores to spaces', () => {
      expect(component.formatActivityAction('login_success')).toBe('login success');
    });

    it('limits to 3 words for long action strings', () => {
      expect(component.formatActivityAction('pending_transactions_processed_extra_words'))
        .toBe('pending transactions processed');
    });
  });

  describe('shortResource', () => {
    it('returns null for null input', () => {
      expect(component.shortResource(null)).toBeNull();
    });

    it('returns short strings unchanged', () => {
      expect(component.shortResource('Plan 80% Jun EUR')).toBe('Plan 80% Jun EUR');
    });

    it('truncates strings over 28 chars', () => {
      const result = component.shortResource('ProcessPendingByPayeeAndPeriodAndMore');
      // slice(0,26) + '…' = 27 chars total
      expect(result).toBe('ProcessPendingByPayeeAndPe…');
      expect(result!.length).toBeLessThanOrEqual(27);
    });
  });

  describe('pendingActionCount', () => {
    it('returns 0 with no summary', () => {
      expect(component.pendingActionCount()).toBe(0);
    });

    it('counts draft pay runs + pending approval items + pending-by-plan totals', () => {
      component.store.summary.set(buildMockSummary({
        draftPayRunsCount: 2,
        payoutsPendingApprovalCount: 5,
        payoutsApprovedUnpaidByCurrency: [{ amount: 1000, currency: 'EUR' }],
        pendingByPlanItems: [
          { planId: 'p1', planName: 'Plan A', currency: 'EUR', pendingCount: 3 },
          { planId: 'p2', planName: 'Plan B', currency: 'USD', pendingCount: 4 },
        ],
      }));
      expect(component.pendingActionCount()).toBe(15); // 2 + 5 + 1 + 3 + 4
    });
  });

  // Transactions blocked because their plan can't be determined. Shown per PAYEE (the cause), but the
  // badge counts blocked TRANSACTIONS, because that's how many things are actually stuck.
  describe('ambiguous attribution', () => {
    const rudolph = {
      payeeId: 'payee-1',
      payeeName: 'Rudolph',
      employeeCode: 'CEO-001',
      transactionCount: 43,
      planNames: ['Plan A', 'Plan B'],
    };

    it('returns an empty list with no summary', () => {
      expect(component.ambiguousAttributionPayees()).toEqual([]);
    });

    it('surfaces one row per payee, not per transaction', () => {
      component.store.summary.set(buildMockSummary({ ambiguousAttributionPayees: [rudolph] }));
      expect(component.ambiguousAttributionPayees().length).toBe(1);
      expect(component.ambiguousAttributionPayees()[0].transactionCount).toBe(43);
    });

    it('makes the attention card non-empty on its own', () => {
      component.store.summary.set(buildMockSummary({ ambiguousAttributionPayees: [rudolph] }));
      expect(component.hasAttentionItems()).toBeTrue();
    });

    it('counts blocked transactions (not payees) in the badge total', () => {
      component.store.summary.set(buildMockSummary({ ambiguousAttributionPayees: [rudolph] }));
      expect(component.attentionBadgeTotal()).toBe(43);
    });

    it('deep-links to the payee, where the overlapping assignments can be fixed', () => {
      expect(component.ambiguousLinkParams(rudolph)).toEqual(['/payees', 'payee-1']);
    });
  });

  describe('pendingByPlanTotalCount', () => {
    it('returns 0 with no summary', () => {
      expect(component.pendingByPlanTotalCount()).toBe(0);
    });

    it('returns 0 when pendingByPlanItems is empty', () => {
      component.store.summary.set(buildMockSummary({ pendingByPlanItems: [] }));
      expect(component.pendingByPlanTotalCount()).toBe(0);
    });

    it('sums pending counts across all plans', () => {
      component.store.summary.set(buildMockSummary({
        pendingByPlanItems: [
          { planId: 'p1', planName: 'Plan A', currency: 'EUR', pendingCount: 7 },
          { planId: 'p2', planName: 'Plan B', currency: 'USD', pendingCount: 3 },
        ],
      }));
      expect(component.pendingByPlanTotalCount()).toBe(10);
    });
  });

  describe('trendBarPoints', () => {
    it('builds 2-bar chart data with prior first, current second', () => {
      const point: DashboardTrendPoint = {
        currency: 'EUR', currentAmount: 1200, priorAmount: 900,
        changePercent: 33.3, direction: 'up',
      };
      const bars = component.trendBarPoints(point, 'May 2026', 'Apr 2026');
      expect(bars.length).toBe(2);
      expect(bars[0]).toEqual({ label: 'Apr 2026', value: 900, currency: 'EUR' });
      expect(bars[1]).toEqual({ label: 'May 2026', value: 1200, currency: 'EUR', isCurrent: true });
    });
  });

  // ── Period link params ────────────────────────────────────────────────────

  describe('_periodDates', () => {
    it('returns null dates for all-time', () => {
      const { from, to } = component._periodDates('all-time');
      expect(from).toBeNull();
      expect(to).toBeNull();
    });

    it('returns null dates for unknown period key', () => {
      const { from, to } = component._periodDates('unknown-key');
      expect(from).toBeNull();
      expect(to).toBeNull();
    });

    it('this-month: from is first of current month', () => {
      const { from } = component._periodDates('this-month');
      const today = new Date();
      const yyyy = today.getFullYear();
      const mm = String(today.getMonth() + 1).padStart(2, '0');
      expect(from).toBe(`${yyyy}-${mm}-01`);
    });

    it('this-month: to is today', () => {
      const { to } = component._periodDates('this-month');
      const today = new Date();
      const yyyy = today.getFullYear();
      const mm = String(today.getMonth() + 1).padStart(2, '0');
      const dd = String(today.getDate()).padStart(2, '0');
      expect(to).toBe(`${yyyy}-${mm}-${dd}`);
    });

    it('ytd: from is Jan 1 of current year', () => {
      const { from } = component._periodDates('ytd');
      expect(from).toBe(`${new Date().getFullYear()}-01-01`);
    });
  });

  describe('payoutsLinkParams', () => {
    it('includes period key', () => {
      component.store.setPeriod('last-month');
      expect(component.payoutsLinkParams()['period']).toBe('last-month');
    });

    it('all-time: no pFrom or pTo', () => {
      component.store.setPeriod('all-time');
      const p = component.payoutsLinkParams();
      expect(p['pFrom']).toBeUndefined();
      expect(p['pTo']).toBeUndefined();
    });

    it('this-month: pFrom is first of month', () => {
      component.store.setPeriod('this-month');
      const today = new Date();
      const yyyy = today.getFullYear();
      const mm = String(today.getMonth() + 1).padStart(2, '0');
      expect(component.payoutsLinkParams()['pFrom']).toBe(`${yyyy}-${mm}-01`);
    });
  });

  describe('transactionsLinkParams', () => {
    it('all-time: returns empty object (no dates)', () => {
      component.store.setPeriod('all-time');
      const p = component.transactionsLinkParams();
      expect(Object.keys(p).length).toBe(0);
    });

    it('this-month: txFrom is first of month', () => {
      component.store.setPeriod('this-month');
      const today = new Date();
      const yyyy = today.getFullYear();
      const mm = String(today.getMonth() + 1).padStart(2, '0');
      expect(component.transactionsLinkParams()['txFrom']).toBe(`${yyyy}-${mm}-01`);
    });
  });

  describe('creditsLinkParams', () => {
    it('all-time: returns empty object', () => {
      component.store.setPeriod('all-time');
      expect(Object.keys(component.creditsLinkParams()).length).toBe(0);
    });

    it('ytd: allocFrom is Jan 1', () => {
      component.store.setPeriod('ytd');
      expect(component.creditsLinkParams()['allocFrom']).toBe(`${new Date().getFullYear()}-01-01`);
    });
  });
});

// ── Action band payout card routing ──────────────────────────────────────────
// Regression guard: dashboard payout cards must link to /payouts (the payout list)
// with the correct status filter — NOT to /pay-runs (a different entity that can
// never show the same global total as a payout-level aggregate).

describe('Action band payout card routing', () => {
  let fixture: ComponentFixture<DashboardComponent>;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [DashboardComponent, TranslateModule.forRoot()],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    });
    fixture = TestBed.createComponent(DashboardComponent);
    fixture.detectChanges();
  });

  it('"Pending Approval" card links to /payouts with status=Calculated (not /pay-runs)', () => {
    const allAs = fixture.debugElement.queryAll(By.css('a.stat-card'));
    const hrefs = allAs.map(a => (a.nativeElement as HTMLAnchorElement).href);

    expect(hrefs.some(h => h.includes('/payouts') && h.includes('status=Calculated')))
      .withContext(`Expected one of: ${hrefs.join(', ')}`)
      .toBeTrue();
  });

  it('"Approved — Not Paid" card links to /payouts with status=Approved (not /pay-runs)', () => {
    const allAs = fixture.debugElement.queryAll(By.css('a.stat-card'));
    const hrefs = allAs.map(a => (a.nativeElement as HTMLAnchorElement).href);

    expect(hrefs.some(h => h.includes('/payouts') && h.includes('status=Approved')))
      .withContext(`Expected one of: ${hrefs.join(', ')}`)
      .toBeTrue();
  });

  it('"Draft Pay Runs" card links to /pay-runs with status=Draft (not /payouts)', () => {
    const allAs = fixture.debugElement.queryAll(By.css('a.stat-card'));
    const hrefs = allAs.map(a => (a.nativeElement as HTMLAnchorElement).href);

    expect(hrefs.some(h => h.includes('/pay-runs') && h.includes('status=Draft')))
      .withContext(`Expected one of: ${hrefs.join(', ')}`)
      .toBeTrue();
  });
});

// ── Deal-lost alerts (revert only for Calculated; Paid is informational) ──────
describe('Dashboard deal-lost alerts', () => {
  let fixture: ComponentFixture<DashboardComponent>;
  let component: DashboardComponent;
  let store: DashboardStore;

  const alert = (status: 'Calculated' | 'Paid', ref: string) => ({
    transactionId: `tx-${ref}`, referenceNumber: ref, externalDealId: '5000',
    transactionStatus: status, commissionAmount: 100, commissionCurrency: 'EUR',
    detectedAt: '2026-07-27T00:00:00Z',
  });

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [DashboardComponent, TranslateModule.forRoot()],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    });
    fixture = TestBed.createComponent(DashboardComponent);
    component = fixture.componentInstance;
    store = TestBed.inject(DashboardStore);
  });

  it('canRevert() is true only for a Calculated commission', () => {
    expect(component.canRevert(alert('Calculated', 'A') as never)).toBeTrue();
    expect(component.canRevert(alert('Paid', 'B') as never)).toBeFalse();
  });

  it('shows a revert button for a Calculated alert but not for a Paid one', () => {
    store.summary.set(buildMockSummary({
      dealLostAlerts: [alert('Calculated', 'HUBSPOT-1'), alert('Paid', 'HUBSPOT-2')] as never,
    }));
    store.loading.set(false); // the attention card is gated on !loading; the init load never resolves here
    fixture.detectChanges();

    const buttons = fixture.debugElement
      .queryAll(By.css('ws-button button'))
      .map(b => (b.nativeElement as HTMLElement).textContent ?? '');
    // Exactly one revert button — for the Calculated alert; the Paid one shows a badge, no button.
    expect(buttons.filter(t => t.includes('DEAL_LOST_REVERT')).length).toBe(1);
  });

  it('askRevert sets the confirmation target; cancelRevert clears it', () => {
    const a = alert('Calculated', 'HUBSPOT-1') as never;
    component.askRevert(a);
    expect(component.revertTarget()).toBe(a);
    component.cancelRevert();
    expect(component.revertTarget()).toBeNull();
  });
});

// ── DashboardStore signal tests ───────────────────────────────────────────────

describe('DashboardStore', () => {
  let store: DashboardStore;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [DashboardStore, provideHttpClient(), provideHttpClientTesting()],
    });
    store = TestBed.inject(DashboardStore);
  });

  it('has default period "this-month"', () => {
    expect(store.period()).toBe('this-month');
  });

  it('updates period via setPeriod', () => {
    store.setPeriod('ytd');
    expect(store.period()).toBe('ytd');
  });

  it('hasPendingActions is false with no summary', () => {
    expect(store.hasPendingActions()).toBe(false);
  });

  it('hasPendingActions is true with draft pay runs', () => {
    store.summary.set(buildMockSummary({ draftPayRunsCount: 2 }));
    expect(store.hasPendingActions()).toBe(true);
  });

  it('hasPendingActions is false when all action counts are zero', () => {
    store.summary.set(buildMockSummary({ draftPayRunsCount: 0, payoutsPendingApprovalCount: 0 }));
    expect(store.hasPendingActions()).toBe(false);
  });

  it('activityFeed returns empty array with no summary', () => {
    expect(store.activityFeed()).toEqual([]);
  });

  it('trendBand returns null with no summary', () => {
    expect(store.trendBand()).toBeNull();
  });
});

// ── Helpers ───────────────────────────────────────────────────────────────────

function buildMockSummary(
  actionOverride: Partial<DashboardSummary['actionBand']> = {}
): DashboardSummary {
  return {
    periodLabel: 'June 2026',
    actionBand: {
      draftPayRunsCount: 0,
      payoutsPendingApprovalCount: 0,
      payoutsPendingApprovalByCurrency: [],
      payoutsApprovedUnpaidByCurrency: [],
      pendingByPlanItems: [],
      unprocessablePendingItems: [],
      driftAlerts: [],
      dealLostAlerts: [],
      ambiguousAttributionPayees: [],
      ...actionOverride,
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
  };
}
