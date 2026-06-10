import { TestBed } from '@angular/core/testing';
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

    it('counts draft pay runs + pending approval items', () => {
      component.store.summary.set(buildMockSummary({
        draftPayRunsCount: 2,
        payoutsPendingApprovalCount: 5,
        payoutsApprovedUnpaidByCurrency: [{ amount: 1000, currency: 'EUR' }],
      }));
      expect(component.pendingActionCount()).toBe(8);
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
