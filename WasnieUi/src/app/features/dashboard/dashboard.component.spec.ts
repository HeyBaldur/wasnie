import { TestBed, ComponentFixture } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Router, provideRouter } from '@angular/router';
import { TranslateModule } from '@ngx-translate/core';
import { DashboardComponent } from './dashboard.component';
import { DashboardActivityItem } from './models/dashboard.models';
import { DashboardStore } from './store/dashboard.store';
import { AuthService } from '../../core/services/auth.service';
import { DashboardSummary, DashboardTrendBand, DashboardTrendPoint } from './models/dashboard.models';

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

  // ── Polymorphic card: one skeleton, two states ─────────────────────────────
  // The card renders ONE structure for both trend and progress — same chart, same axis, same badge
  // geometry, same footer. Only text and colour differ, and these helpers are where that lives.
  describe('badge and footer polymorphism', () => {
    const pacingPoint = (pacingPercent: number | null): DashboardTrendPoint => ({
      currency: 'EUR', currentAmount: 500, priorAmount: 4939,
      changePercent: null, direction: 'pacing', pacingPercent,
    });
    const closedPoint = (
      changePercent: number | null,
      direction: DashboardTrendPoint['direction'],
    ): DashboardTrendPoint => ({
      currency: 'EUR', currentAmount: 4939, priorAmount: 181715,
      changePercent, direction,
    });

    // buildMockSummary only overrides the action band and leaves trendBand null, so the band is set
    // explicitly here — isPacing is exactly what the helpers under test read.
    const setPacing = (isPacing: boolean) => {
      const summary = buildMockSummary();
      summary.trendBand = {
        commissionTrend: [],
        isPacing,
        currentFrom: '2026-08-01',
        currentTo: '2026-08-05',
        priorFrom: '2026-07-01',
        priorTo: '2026-07-31',
      };
      component.store.summary.set(summary);
    };

    it('reports a baseline when the previous period had a total', () => {
      expect(component.hasPacingBase(pacingPoint(10.12))).toBeTrue();
    });

    it('reports no baseline when the previous total was zero', () => {
      expect(component.hasPacingBase(pacingPoint(null))).toBeFalse();
    });

    it('rounds the pacing percentage for display', () => {
      expect(component.pacingPercent(pacingPoint(10.12))).toBe(10);
      expect(component.pacingPercent(pacingPoint(10.62))).toBe(11);
    });

    it('treats reaching the baseline as exceeded (a positive outcome)', () => {
      expect(component.pacingExceeded(pacingPoint(100))).toBeTrue();
      expect(component.pacingExceeded(pacingPoint(99.9))).toBeFalse();
    });

    // ★ THE GUARD: a running period can never render the red badge or the arrow.
    it('never shows a negative badge or an arrow while pacing', () => {
      setPacing(true);
      for (const pct of [0, 10, 99.9, 100, 150, null]) {
        const p = pacingPoint(pct);
        expect(component.badgeIsNegative(p)).withContext(`pacing ${pct}`).toBeFalse();
        expect(component.badgeShowsArrow(p)).withContext(`pacing ${pct}`).toBeFalse();
      }
    });

    it('shows a positive badge while pacing only once the baseline is beaten', () => {
      setPacing(true);
      expect(component.badgeIsPositive(pacingPoint(99))).toBeFalse();
      expect(component.badgeIsPositive(pacingPoint(120))).toBeTrue();
      expect(component.badgeIsPositive(pacingPoint(null))).toBeFalse();
    });

    it('uses the pacing badge text when there is a baseline, "New" when there is none', () => {
      setPacing(true);
      expect(component.badgeText(pacingPoint(10))).toBe('DASHBOARD.PACING_PILL');
      expect(component.badgeParams(pacingPoint(10))).toEqual({ percent: 10 });
      expect(component.badgeText(pacingPoint(null))).toBe('DASHBOARD.TREND_NO_BASE');
      expect(component.badgeParams(pacingPoint(null))).toEqual({});
    });

    it('keeps the arrow and the red badge for a CLOSED period', () => {
      setPacing(false);
      const down = closedPoint(-97.3, 'down');
      expect(component.badgeIsNegative(down)).toBeTrue();
      expect(component.badgeShowsArrow(down)).toBeTrue();
      expect(component.badgeText(down)).toBe('-97.3%');

      const up = closedPoint(12.5, 'up');
      expect(component.badgeIsPositive(up)).toBeTrue();
    });

    it('falls back to "New" for a closed period with no usable base', () => {
      setPacing(false);
      const noBase = closedPoint(null, 'neutral');
      expect(component.badgeText(noBase)).toBe('DASHBOARD.TREND_NO_BASE');
      expect(component.badgeShowsArrow(noBase)).toBeFalse();
    });

    // The footer used to be a label plus a bare amount, and its wording was the only thing that
    // changed between the two states. It is now a sentence that says WHAT the figure is — the card
    // counts money that moved, not commission created — and it still carries the baseline amount the
    // pill is a percentage of. The two old label keys went with it.

    // The chart is the same call in both states — that is what keeps the axis and bars from moving.
    it('builds the same two-bar structure for both states', () => {
      const bars = component.trendBarPoints(pacingPoint(10), 'August 2026', 'July 2026');
      expect(bars.length).toBe(2);
      expect(bars[0].label).toBe('July 2026');
      expect(bars[0].isCurrent).toBeFalsy();
      expect(bars[1].label).toBe('August 2026');
      expect(bars[1].isCurrent).toBeTrue();
    });
  });

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

  /**
   * ★★ THESE TWO TESTS USED TO PIN THE DEFECT (KAN-19). They asserted that
   * `formatActivityAction` turned `login_success` into "login success" and truncated anything
   * longer to three words — which is exactly what was wrong: the feed built ENGLISH PROSE out of an
   * action CODE in the browser, so `PLAN_CLAWBACK_POLICY_CHANGED` reached the reader as "plan
   * clawback policy" (the verb, the entire meaning, was what got cut) and stayed English in Spanish
   * and Polish. Both were green the whole time, because they measured the implementation rather
   * than the requirement. They are replaced, not deleted, so the history of what changed is here.
   */
  describe('activityActionKey', () => {
    it('maps an action code to a translation key, never to invented words', () => {
      expect(component.activityActionKey('LOGIN_SUCCESS')).toBe('AUDIT.ACTION.LOGIN_SUCCESS');
    });

    /** The case the old prose builder silently mangled: the verb survives. */
    it('keeps the whole meaning of a long code', () => {
      expect(component.activityActionKey('PLAN_CLAWBACK_POLICY_CHANGED'))
        .toBe('AUDIT.ACTION.PLAN_CLAWBACK_POLICY_CHANGED');
    });

    it('falls back to the generic label rather than leaking an unknown code', () => {
      expect(component.activityActionKey('SOMETHING_NEW')).toBe('AUDIT.ACTION.UNKNOWN');
    });
  });

  describe('activityLink', () => {
    const item = (over: Partial<DashboardActivityItem> = {}): DashboardActivityItem => ({
      timestampUtc: '2026-09-07T07:00:28Z',
      actorEmail: 'admin@wasnie.test',
      actorInitials: 'AD',
      action: 'PLAN_ARCHIVED',
      resourceType: 'Plan',
      resourceId: 'plan-1',
      resourceDisplayName: 'EU Accelerator',
      ...over,
    });

    it('links an entry to the entity it changed', () => {
      expect(component.activityLink(item())).toEqual(['/plans', 'plan-1']);
    });

    /** ★ A sign-in has no entity to open, so it gets no link rather than a dead one. */
    it('gives no link where the resource type has no screen', () => {
      expect(component.activityLink(item({ resourceType: 'Auth', action: 'LOGIN_SUCCESS' }))).toBeNull();
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

  // Departed payees whose account never closed. The engine stops processing them — correct — which
  // is also why nothing else on any screen would ever mention the money still sitting there.
  describe('terminated accounts pending settlement', () => {
    // `unsettled` is commission earned and never paid. It leaves the ledger balance at zero — the
    // ledger records what someone OWES — so it is its own bucket, not a slice of the two balance ones.
    const row = (payeeId: string, balance: number, unsettled = 0) => ({
      payeeId,
      payeeName: `Payee ${payeeId}`,
      employeeCode: payeeId.toUpperCase(),
      terminationDate: '2026-06-30',
      balance,
      currency: 'EUR',
      balanceUpdatedAt: balance === 0 ? null : '2026-07-29T00:00:00Z',
      accountClosedAt: null,
      unsettledCreditTotal: unsettled,
      unsettledCredits: unsettled === 0 ? [] : [{
        creditId: `credit-${payeeId}`,
        amount: unsettled,
        currency: 'EUR',
        planName: 'EU Accelerator Q2 2026',
        ruleName: 'Tier 1: 4% up to quota',
        allocatedAt: '2026-08-27',
        transactionId: `tx-${payeeId}`,
        transactionReference: `POL-${payeeId}`,
      }],
    });

    it('shows nothing to do when no departed payee has an open account', () => {
      component.terminated.rows.set([]);

      expect(component.terminated.count()).toBe(0);
      expect(component.terminated.owedToPayeesCount()).toBe(0);
      expect(component.terminated.owedByPayeesCount()).toBe(0);
    });

    it('counts exactly the rows the server returned, both signs', () => {
      // The endpoint decides what is "still open"; the card only counts what came back.
      component.terminated.rows.set([row('a', -400), row('b', 500)]);

      expect(component.terminated.count()).toBe(2);
    });

    it('keeps money owed TO a payee apart from money owed BY one', () => {
      component.terminated.rows.set([row('a', -400), row('b', 500), row('c', -250)]);

      expect(component.terminated.owedToPayeesCount())
        .withContext('a liability: treasury still has to pay them').toBe(1);
      expect(component.terminated.owedByPayeesCount())
        .withContext('debt to recover or write off').toBe(2);
    });

    // ★ THE ROW THAT USED TO BE INVISIBLE. Unpaid commission puts a departed payee on the queue with
    // a ledger balance of exactly zero, so it belongs to neither balance bucket — and the card has to
    // account for it or its own numbers stop adding up to its own total.
    it('counts unpaid commission as its own bucket, outside both balance buckets', () => {
      component.terminated.rows.set([row('a', -400), row('b', 0, 3869.34)]);

      expect(component.terminated.count()).toBe(2);
      expect(component.terminated.owedByPayeesCount()).toBe(1);
      expect(component.terminated.owedToPayeesCount()).toBe(0);
      expect(component.terminated.unsettledCreditCount()).toBe(1);
    });

    it('adds open accounts to the band badge so the header does not claim all-clear', () => {
      component.store.summary.set(buildMockSummary({
        draftPayRunsCount: 0,
        payoutsPendingApprovalCount: 0,
        payoutsApprovedUnpaidByCurrency: [],
        pendingByPlanItems: [],
      }));
      component.terminated.rows.set([row('a', -400)]);

      expect(component.pendingActionCount()).toBe(1);
    });

    it('does not inflate the badge when every account is settled', () => {
      component.store.summary.set(buildMockSummary({
        draftPayRunsCount: 2,
        payoutsPendingApprovalCount: 0,
        payoutsApprovedUnpaidByCurrency: [],
        pendingByPlanItems: [],
      }));
      component.terminated.rows.set([]);

      expect(component.pendingActionCount()).toBe(2);
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

  // ── Range filter and link params ──────────────────────────────────────────

  describe('date range filter', () => {
    // The filter is a WsDateRangePicker (a ControlValueAccessor), so the range travels through a
    // FormControl rather than a [value]/(valueChange) pair. These pin both directions of that wiring.
    it('the range control starts on the the current store range', () => {
      expect(component.rangeControl.value).toEqual({
        start: component.store.range().from,
        end: component.store.range().to,
      });
    });

    it('picking a range in the control reaches the store', () => {
      component.rangeControl.setValue({ start: '2026-02-01', end: '2026-04-15' });
      expect(component.store.range()).toEqual({ from: '2026-02-01', to: '2026-04-15' });
    });

    it('opens on the WHOLE current month, not the month so far', () => {
      const today = new Date();
      const first = new Date(today.getFullYear(), today.getMonth(), 1);
      const last = new Date(today.getFullYear(), today.getMonth() + 1, 0);
      const iso = (d: Date) =>
        `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;

      expect(component.store.range()).toEqual({ from: iso(first), to: iso(last) });
    });

    // ★ A backwards range would ask the server for a window it refuses. The picker already orders the
    //   two ends, so this is the second of three guards — pinned because it is the cheap one to lose.
    it('a backwards range is ignored rather than applied', () => {
      const before = component.store.range();
      component.store.setRange({ from: '2026-04-15', to: '2026-02-01' });
      expect(component.store.range()).toEqual(before);
    });

    it('an incomplete range is ignored', () => {
      const before = component.store.range();
      component.onRangeChange({ start: '2026-02-01', end: null });
      expect(component.store.range()).toEqual(before);
    });

    it('resetting returns to the current month', () => {
      component.store.setRange({ from: '2026-02-01', to: '2026-04-15' });
      component.resetRange();

      const today = new Date();
      expect(component.store.range().from)
        .toBe(`${today.getFullYear()}-${String(today.getMonth() + 1).padStart(2, '0')}-01`);
    });

    it('a single-day range is accepted', () => {
      component.store.setRange({ from: '2026-03-09', to: '2026-03-09' });
      expect(component.store.range()).toEqual({ from: '2026-03-09', to: '2026-03-09' });
    });
  });

  describe('payoutsLinkParams', () => {
    it('carries the selected range as the payment window', () => {
      component.store.setRange({ from: '2026-02-01', to: '2026-04-15' });
      const p = component.payoutsLinkParams();

      expect(p['payFrom']).toBe('2026-02-01');
      expect(p['payTo']).toBe('2026-04-15');
    });

    it('no longer sends a period key — there is no period to send', () => {
      component.store.setRange({ from: '2026-02-01', to: '2026-04-15' });
      expect(component.payoutsLinkParams()['period']).toBeUndefined();
    });

    // The card is cash flow, so the list it opens has to be filtered the same way the card sums: by
    // PAYMENT date and Paid only. Linking with the compensation period (pFrom/pTo) instead would open a
    // list whose total contradicts the figure the user just clicked.
    it('filters the destination list by payment date, not by compensation period', () => {
      component.store.setRange({ from: '2026-07-01', to: '2026-07-31' });
      const p = component.payoutsLinkParams();

      expect(p['payFrom']).toBeDefined();
      expect(p['payTo']).toBeDefined();
      expect(p['pFrom']).toBeUndefined();
      expect(p['pTo']).toBeUndefined();
    });

    // ★ The card→list contract, pinned. The list total must equal the card figure, which only holds if
    // the link carries the PAYMENT window plus Status=Paid. A silent loss of any of these three is the
    // difference between a list that matches the number clicked and one that does not.
    it('carries exactly the three params the list needs to match the card', () => {
      component.store.setRange({ from: '2026-07-01', to: '2026-07-31' });
      const p = component.payoutsLinkParams();

      expect(p['status']).toBe('Paid');
      expect(p['payFrom']).toBeDefined();
      expect(p['payTo']).toBeDefined();
    });

    it('never links by compensation period, which would not match the card total', () => {
      for (const range of [
        { from: '2026-08-01', to: '2026-08-31' },
        { from: '2026-07-01', to: '2026-07-31' },
        { from: '2026-02-01', to: '2026-04-15' },
        { from: '2026-01-01', to: '2026-12-31' },
      ]) {
        component.store.setRange(range);
        const p = component.payoutsLinkParams();
        const ctx = `${range.from}..${range.to}`;
        expect(p['pFrom']).withContext(ctx).toBeUndefined();
        expect(p['pTo']).withContext(ctx).toBeUndefined();
        expect(p['status']).withContext(ctx).toBe('Paid');
      }
    });

    it('the payment window matches the range the card is showing', () => {
      component.store.setRange({ from: '2026-07-01', to: '2026-07-31' });
      const p = component.payoutsLinkParams();

      expect(p['payFrom']).toBe(component.store.range().from);
      expect(p['payTo']).toBe(component.store.range().to);
    });

    it('restricts the destination list to Paid payouts', () => {
      component.store.setRange({ from: '2026-07-01', to: '2026-07-31' });
      expect(component.payoutsLinkParams()['status']).toBe('Paid');
    });
  });

  // ── Chart bar drill-down ───────────────────────────────────────────────────
  // Every figure on screen must be traceable to the rows behind it. Card and bars share ONE link
  // definition, so they cannot point at different things.
  describe('onTrendBarClick', () => {
    let navigateSpy: jasmine.Spy;

    const bandWith = (over: Partial<DashboardTrendBand> = {}): DashboardTrendBand => ({
      commissionTrend: [],
      isPacing: true,
      currentFrom: '2026-08-01',
      currentTo: '2026-08-05',
      priorFrom: '2026-07-01',
      priorTo: '2026-07-31',
      ...over,
    });

    const setBand = (band: DashboardTrendBand | null) => {
      const summary = buildMockSummary();
      summary.trendBand = band;
      component.store.summary.set(summary);
    };

    beforeEach(() => {
      navigateSpy = spyOn(TestBed.inject(Router), 'navigate').and.resolveTo(true);
    });

    it('drills the PRIOR bar down to the prior window', () => {
      setBand(bandWith());
      component.onTrendBarClick({ label: 'July 2026', value: 4939, currency: 'EUR' });

      expect(navigateSpy).toHaveBeenCalledWith(['/payouts'], {
        queryParams: { status: 'Paid', payFrom: '2026-07-01', payTo: '2026-07-31' },
      });
    });

    it('drills the CURRENT bar down to the current window', () => {
      setBand(bandWith());
      component.onTrendBarClick({ label: 'August 2026', value: 500, currency: 'EUR', isCurrent: true });

      expect(navigateSpy).toHaveBeenCalledWith(['/payouts'], {
        queryParams: { status: 'Paid', payFrom: '2026-08-01', payTo: '2026-08-05' },
      });
    });

    it('uses the same link definition as the card', () => {
      // If these ever diverge, a bar and the card above it would open different lists.
      setBand(bandWith());
      component.onTrendBarClick({ label: 'August 2026', value: 500, currency: 'EUR', isCurrent: true });

      const fromBar = navigateSpy.calls.mostRecent().args[1].queryParams;
      const fromHelper = component.payoutsLinkParamsFor('2026-08-01', '2026-08-05');
      expect(fromBar).toEqual(fromHelper);
    });

    it('does nothing when there is no trend band', () => {
      setBand(null);
      component.onTrendBarClick({ label: 'x', value: 1, currency: 'EUR', isCurrent: true });
      expect(navigateSpy).not.toHaveBeenCalled();
    });

    it('does not navigate on an empty window', () => {
      // Opening the whole table is not a drill-down. The window can no longer be NULL — a free range
      // always has a predecessor, unlike the retired "all-time" — so what is guarded now is a
      // malformed payload rather than a period that legitimately had no bounds.
      setBand(bandWith({ priorFrom: '', priorTo: '' }));
      component.onTrendBarClick({ label: 'x', value: 1, currency: 'EUR' });
      expect(navigateSpy).not.toHaveBeenCalled();
    });
  });

  describe('transactionsLinkParams', () => {
    it('carries the selected range as the transaction window', () => {
      component.store.setRange({ from: '2026-02-01', to: '2026-04-15' });
      const p = component.transactionsLinkParams();
      expect(p.txFrom).toBe('2026-02-01');
      expect(p.txTo).toBe('2026-04-15');
    });
  });

  describe('creditsLinkParams', () => {
    it('carries the selected range as the allocation window', () => {
      component.store.setRange({ from: '2026-02-01', to: '2026-04-15' });
      const p = component.creditsLinkParams();
      expect(p.allocFrom).toBe('2026-02-01');
      expect(p.allocTo).toBe('2026-04-15');
    });

    // The commission cards sum credits by ALLOCATION date, so their deep link must filter on the same
    // field and the same window, or the list will not add up to the figure that was clicked.
    it('the commission cards link on the same window and the same date field', () => {
      component.store.setRange({ from: '2026-02-01', to: '2026-04-15' });

      for (const card of component.commissionCards) {
        const p = component.commissionsLinkParams(card.settlement);
        expect(p['allocFrom']).withContext(card.key).toBe('2026-02-01');
        expect(p['allocTo']).withContext(card.key).toBe('2026-04-15');
      }
    });

    it('each card carries the settlement filter the credits screen applies', () => {
      // It used to send `paid=true|false`, which nothing on the destination read.
      expect(component.commissionsLinkParams('Payable')['settlement']).toBe('Payable');
      expect(component.commissionsLinkParams('Paid')['settlement']).toBe('Paid');
      expect(component.commissionsLinkParams('Unpaid')['settlement']).toBe('Unpaid');
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

  // ── The card counts all-time; the link has to say so ───────────────────────
  //
  // The card's number comes from a query with NO date filter, but Pay Runs applies "this month"
  // whenever the URL carries no `period`. Without this param the card promised 2 drafts and the list
  // showed 0, because the real drafts sat in September and October. This is the whole fix: if the
  // param is ever dropped again, the count and the list silently disagree once more.
  it('"Draft Pay Runs" link carries period=all-time, so the list matches the all-time count', () => {
    const allAs = fixture.debugElement.queryAll(By.css('a.stat-card'));
    const payRuns = allAs
      .map(a => (a.nativeElement as HTMLAnchorElement).href)
      .find(h => h.includes('/pay-runs'));

    expect(payRuns).withContext('no /pay-runs card link found').toBeDefined();
    expect(payRuns).toContain('period=all-time');
  });

  // ── And the payout cards must NOT copy it ──────────────────────────────────
  //
  // Payouts solves the same problem the other way round: it applies its default period ONLY when the
  // URL has no params at all, so arriving with `?status=…` already yields an all-time list. Adding a
  // period here would be harmless-looking symmetry that pins a filter the destination never wanted.
  it('payout cards deliberately send NO period — their screen already skips its default', () => {
    const allAs = fixture.debugElement.queryAll(By.css('a.stat-card'));
    const payouts = allAs
      .map(a => (a.nativeElement as HTMLAnchorElement).href)
      .filter(h => h.includes('/payouts'));

    expect(payouts.length).withContext('no /payouts card links found').toBeGreaterThan(0);
    payouts.forEach(h => expect(h).not.toContain('period='));
  });
});

// ── Deal-lost alerts (revert only for Calculated; Paid is informational) ──────
describe('Dashboard deal-lost alerts', () => {
  let fixture: ComponentFixture<DashboardComponent>;
  let component: DashboardComponent;
  let store: DashboardStore;

  const alert = (
    status: 'Calculated' | 'Paid',
    ref: string,
    clawbackState: 'NotApplicable' | 'Applied' | 'Pending' = 'NotApplicable',
  ) => ({
    transactionId: `tx-${ref}`, referenceNumber: ref, externalDealId: '5000',
    // transactionStatus is the LIVE status from the server; statusAtDetection is the old snapshot.
    transactionStatus: status, statusAtDetection: 'Calculated', clawbackState,
    commissionAmount: 100, commissionCurrency: 'EUR',
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

  // ── The sentence has to track the button ───────────────────────────────────
  // A row that offers no revert must not keep saying "you can revert (it has not been paid)". These
  // pin the exact text keys, because the defect was never the button — it was the claim next to it.

  it('an unpaid commission still reads as revertible', () => {
    expect(component.dealLostActionKey(alert('Calculated', 'A') as never))
      .toBe('DASHBOARD.DEAL_LOST_ACTION_CALCULATED');
  });

  it('a PAID commission whose clawback already ran says so — never "not paid"', () => {
    expect(component.dealLostActionKey(alert('Paid', 'B', 'Applied') as never))
      .toBe('DASHBOARD.DEAL_LOST_ACTION_PAID_CLAWBACK_APPLIED');
  });

  it('a PAID commission whose clawback has not run yet reads as pending', () => {
    expect(component.dealLostActionKey(alert('Paid', 'C', 'Pending') as never))
      .toBe('DASHBOARD.DEAL_LOST_ACTION_PAID_CLAWBACK_PENDING');
  });

  it('a commission in any other state claims nothing and offers nothing', () => {
    const cancelled = { ...alert('Paid', 'D'), transactionStatus: 'Cancelled' } as never;
    expect(component.canRevert(cancelled)).toBeFalse();
    expect(component.dealLostActionKey(cancelled)).toBe('DASHBOARD.DEAL_LOST_ACTION_OTHER');
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

  it('defaults to the whole current month', () => {
    const today = new Date();
    const iso = (d: Date) =>
      `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;

    expect(store.range()).toEqual({
      from: iso(new Date(today.getFullYear(), today.getMonth(), 1)),
      to: iso(new Date(today.getFullYear(), today.getMonth() + 1, 0)),
    });
  });

  it('updates the range via setRange', () => {
    store.setRange({ from: '2026-02-01', to: '2026-04-15' });
    expect(store.range()).toEqual({ from: '2026-02-01', to: '2026-04-15' });
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
    from: '2026-06-01',
    to: '2026-06-30',
    commissionsBand: {
      totalByCurrency: [],
      paidByCurrency: [],
      unpaidByCurrency: [],
      closedTotalByCurrency: [], unreachableTotalByCurrency: [],
    },
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
      plansWithoutLiveRules: [],
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

// ── Greeting header ───────────────────────────────────────────────────────────
// The Dashboard header greets the person instead of naming the screen. Two things decide what it
// says: the browser's hour (never the server's — the greeting is about where the user is sitting)
// and the name, which is NOT on /auth/me and therefore arrives from the profile endpoint.

describe('DashboardComponent greeting', () => {
  let httpMock: HttpTestingController;

  /** Builds the component with the clock frozen at `hour` local time. */
  function createAt(hour: number): DashboardComponent {
    jasmine.clock().install();
    jasmine.clock().mockDate(new Date(2026, 7, 28, hour, 30, 0));
    TestBed.configureTestingModule({
      imports: [DashboardComponent, TranslateModule.forRoot()],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        // The shell pulls in stores that read tenantId/isAuthenticated, so the stub answers those too.
        {
          provide: AuthService,
          useValue: {
            currentUser: () => ({ email: 'rudy@acme.test', tenantId: 't1' }),
            tenantId: () => 't1',
            isAuthenticated: () => true,
            getAccessToken: () => null,
          },
        },
      ],
    });
    httpMock = TestBed.inject(HttpTestingController);
    return TestBed.createComponent(DashboardComponent).componentInstance;
  }

  /** Answers the profile GET the constructor fires. Pass '' for a user who never set a name. */
  function flushProfile(firstName: string): void {
    const reqs = httpMock.match(r => r.url.endsWith('/profile'));
    reqs.forEach(r => r.flush({
      firstName, lastName: '', email: 'rudy@acme.test',
      hasPendingEmailChange: false, companyName: 'Acme', organizationSlug: 'acme',
    }));
  }

  afterEach(() => jasmine.clock().uninstall());

  it('greets with morning before noon', () => {
    expect(createAt(8).greetingKey()).toBe('DASHBOARD.GREETING_MORNING');
  });

  it('switches to afternoon exactly at noon', () => {
    expect(createAt(12).greetingKey()).toBe('DASHBOARD.GREETING_AFTERNOON');
  });

  it('switches to evening exactly at 18:00', () => {
    expect(createAt(18).greetingKey()).toBe('DASHBOARD.GREETING_EVENING');
  });

  it('still greets in the evening just before midnight', () => {
    expect(createAt(23).greetingKey()).toBe('DASHBOARD.GREETING_EVENING');
  });

  it('uses the first name from the profile once it arrives', () => {
    const component = createAt(9);
    flushProfile('Rudy');
    expect(component.greetingName()).toBe('Rudy');
    expect(component.greetingNamePart()).toBe(', Rudy');
  });

  it('falls back to the capitalised email local part when no name is on file', () => {
    const component = createAt(9);
    flushProfile('');
    expect(component.greetingName()).toBe('Rudy');
  });

  it('leaves the name clause empty rather than greeting a dangling comma', () => {
    TestBed.resetTestingModule();
    jasmine.clock().install();
    jasmine.clock().mockDate(new Date(2026, 7, 28, 9, 0, 0));
    TestBed.configureTestingModule({
      imports: [DashboardComponent, TranslateModule.forRoot()],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        {
          provide: AuthService,
          useValue: {
            currentUser: () => null,
            tenantId: () => null,
            isAuthenticated: () => false,
            getAccessToken: () => null,
          },
        },
      ],
    });
    const component = TestBed.createComponent(DashboardComponent).componentInstance;
    expect(component.greetingName()).toBe('');
    expect(component.greetingNamePart()).toBe('');
  });
});
