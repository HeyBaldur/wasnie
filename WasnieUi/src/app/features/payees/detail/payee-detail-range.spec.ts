import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { ActivatedRoute, provideRouter } from '@angular/router';
import { TranslateModule } from '@ngx-translate/core';
import { of } from 'rxjs';
import { PayeeDetailComponent } from './payee-detail.component';
import { PayeesApiService } from '../services/payees.api.service';
import { PayeesStore } from '../state/payees.store';
import { ToastService } from '../../../shared/services/toast.service';
import { CurrentUserService } from '../../../core/auth/current-user.service';
import { PayeeDashboard } from '../models/payee-dashboard.model';

/**
 * KAN-63 — the payee Overview takes a date range, its blocks collapse, and it carries the three
 * commission cards.
 *
 * ★ THROUGH THE DOM where the criterion is about the screen. A test on the signals would pass just as
 * happily with the markup wired to a branch that never renders (§A3).
 */

const EMPTY_PAGE = {
  items: [], totalCount: 0, page: 1, pageSize: 10, totalPages: 0,
  hasNextPage: false, hasPreviousPage: false,
};

function dashboard(over: Partial<PayeeDashboard> = {}): PayeeDashboard {
  return {
    from: '2026-08-01',
    to: '2026-08-31',
    commissionsBand: {
      totalByCurrency: [{ amount: 150, currency: 'EUR' }],
      paidByCurrency: [{ amount: 100, currency: 'EUR' }],
      unpaidByCurrency: [{ amount: 50, currency: 'EUR' }],
      closedTotalByCurrency: [], unreachableTotalByCurrency: [],
    },
    attainmentItems: [],
    salesTrend: [],
    recentQuotas: [],
    recentAssignments: [],
    ...over,
  } as PayeeDashboard;
}

describe('PayeeDetailComponent — date range, cards and collapsible blocks', () => {
  let fixture: ComponentFixture<PayeeDetailComponent>;
  let component: PayeeDetailComponent;
  let api: jasmine.SpyObj<PayeesApiService>;

  const el = () => fixture.nativeElement as HTMLElement;

  const iso = (d: Date) =>
    `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;

  beforeEach(async () => {
    api = jasmine.createSpyObj<PayeesApiService>('PayeesApiService', [
      'getPayee', 'getPayeeDashboard', 'getPayeeAssignments', 'getPayeeQuotas', 'getPayeeCredits',
    ]);
    api.getPayeeAssignments.and.returnValue(of(EMPTY_PAGE) as never);
    api.getPayeeQuotas.and.returnValue(of(EMPTY_PAGE) as never);
    api.getPayeeCredits.and.returnValue(of(EMPTY_PAGE) as never);
    api.getPayeeDashboard.and.returnValue(of(dashboard()) as never);
    api.getPayee.and.returnValue(of(null) as never);

    await TestBed.configureTestingModule({
      imports: [PayeeDetailComponent, TranslateModule.forRoot()],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: PayeesApiService, useValue: api },
        {
          provide: PayeesStore,
          // Every store member the template reads. A missing one throws during change detection and
          // every test in the file fails with the same unrelated TypeError.
          useValue: jasmine.createSpyObj('PayeesStore', ['loadPayee'], {
            selectedPayee: () => ({ id: 'payee-1', fullName: 'Ada', isActive: true, status: 'Active' }),
            loading: () => false,
            error: () => null,
          }),
        },
        { provide: ToastService, useValue: jasmine.createSpyObj('ToastService', ['show']) },
        { provide: CurrentUserService, useValue: { hasPermission: () => true } },
        {
          provide: ActivatedRoute,
          useValue: {
            snapshot: { paramMap: new Map([['payeeId', 'payee-1']]), queryParamMap: new Map() },
            paramMap: of(new Map()),
            queryParams: of({}),
          },
        },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(PayeeDetailComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
    // loadOverview() awaits, so the blocks are still skeletons on the first pass. Without this the DOM
    // assertions below would be looking at a loading state and finding nothing.
    await settle();
  });

  /**
   * The loads `await firstValueFrom(of(...))`, so they finish on a microtask — draining the queue is
   * all that is needed. `whenStable()` is NOT usable here: it never resolves for this component, and
   * every test then dies on its own timeout instead of on its assertion.
   */
  async function settle(): Promise<void> {
    for (let i = 0; i < 5; i++) await Promise.resolve();
    fixture.detectChanges();
  }

  // ── the range ─────────────────────────────────────────────────────────────

  it('opens on the WHOLE current month, not the month so far', () => {
    const today = new Date();
    expect(component.range()).toEqual({
      from: iso(new Date(today.getFullYear(), today.getMonth(), 1)),
      to: iso(new Date(today.getFullYear(), today.getMonth() + 1, 0)),
    });
  });

  it('asks the server for that range, not for a preset', () => {
    const [, from, to] = api.getPayeeDashboard.calls.mostRecent().args;
    expect(from).toBe(component.range().from);
    expect(to).toBe(component.range().to);
  });

  it('sends the range to the paginated blocks as an explicit window', () => {
    // ★ These endpoints take dateFrom/dateTo. Sending `period` instead would make the server apply its
    //   own default and the blocks would disagree with the picker above them.
    const quotaArgs = api.getPayeeQuotas.calls.mostRecent().args[1]!;
    expect(quotaArgs.dateFrom).toBe(component.range().from);
    expect(quotaArgs.dateTo).toBe(component.range().to);
    expect(quotaArgs.period).toBeUndefined();
  });

  it('a backwards or incomplete range is ignored', () => {
    const before = component.range();

    component.onRangeChange({ start: '2026-04-15', end: '2026-02-01' });
    expect(component.range()).toEqual(before);

    component.onRangeChange({ start: '2026-02-01', end: null });
    expect(component.range()).toEqual(before);
  });

  it('applying a range refetches every block', () => {
    api.getPayeeDashboard.calls.reset();
    api.getPayeeQuotas.calls.reset();

    component.onRangeChange({ start: '2026-02-01', end: '2026-04-15' });

    expect(component.range()).toEqual({ from: '2026-02-01', to: '2026-04-15' });
    expect(api.getPayeeDashboard).toHaveBeenCalled();
    expect(api.getPayeeQuotas).toHaveBeenCalled();
  });

  // ── the blocks ────────────────────────────────────────────────────────────

  it('every block starts collapsed', () => {
    for (const block of ['attainment', 'trend', 'quotas', 'assignments', 'credits'] as const) {
      expect(component.isBlockOpen(block)).withContext(block).toBeFalse();
      expect(el().querySelector<HTMLElement>(`#pd-block-${block}`)!.hidden)
        .withContext(block).toBeTrue();
    }
  });

  it('the caret opens and closes a block, and only that one', () => {
    const toggle = el().querySelector<HTMLButtonElement>('.bento-card__toggle')!;

    toggle.click();
    fixture.detectChanges();
    expect(component.isBlockOpen('attainment')).toBeTrue();
    expect(component.isBlockOpen('quotas')).withContext('blocks are independent').toBeFalse();

    toggle.click();
    fixture.detectChanges();
    expect(component.isBlockOpen('attainment')).toBeFalse();
  });

  it('every toggle is a real button that announces what it controls', () => {
    const toggles = el().querySelectorAll('.bento-card__toggle');

    expect(toggles.length).toBe(5);
    toggles.forEach(t => {
      expect(t.tagName).toBe('BUTTON');
      expect(t.getAttribute('aria-expanded')).toBe('false');
      expect(t.getAttribute('aria-controls')).toMatch(/^pd-block-/);
    });
  });

  it('the open state does not survive a fresh load of the page', () => {
    component.toggleBlock('quotas');
    expect(component.isBlockOpen('quotas')).toBeTrue();

    const second = TestBed.createComponent(PayeeDetailComponent);
    second.detectChanges();

    expect(second.componentInstance.isBlockOpen('quotas'))
      .withContext('it must reopen collapsed every time').toBeFalse();
  });

  // ── the commission cards ──────────────────────────────────────────────────

  it('carries the same three commission cards as the dashboard', () => {
    expect(component.commissionCards.map(c => c.key)).toEqual(['total', 'paid', 'unpaid']);
    expect(component.amountFor(component.commissionTotals('total'), 'EUR')).toBe(150);
    expect(component.amountFor(component.commissionTotals('paid'), 'EUR')).toBe(100);
    expect(component.amountFor(component.commissionTotals('unpaid'), 'EUR')).toBe(50);
  });

  it('each card links to THIS payee, on the same window and date field it sums', () => {
    component.onRangeChange({ start: '2026-02-01', end: '2026-04-15' });

    for (const card of component.commissionCards) {
      const p = component.commissionsLinkParams(card.settlement);
      expect(p['payeeIds']).withContext(card.key).toBe('payee-1');
      expect(p['allocFrom']).toBe('2026-02-01');
      expect(p['allocTo']).toBe('2026-04-15');
      expect(p['settlement']).toBe(card.settlement);
    }
  });

  it('a payee with no commissions shows zero rather than blowing up on a missing currency', async () => {
    api.getPayeeDashboard.and.returnValue(of(dashboard({
      commissionsBand: {
        totalByCurrency: [], paidByCurrency: [], unpaidByCurrency: [], closedTotalByCurrency: [], unreachableTotalByCurrency: [],
      },
    })) as never);

    component.onRangeChange({ start: '2026-02-01', end: '2026-02-28' });
    await settle();

    expect(component.commissionsCurrency()).toBeNull();
    expect(component.fmtCompact(0, '')).toBe('0');
  });

  // ── the bug the ticket names ──────────────────────────────────────────────

  it('no untranslated DASHBOARD.PERIOD_* key can reach the screen any more', () => {
    // The presets rendered their own keys — three of them never had a translation at all. The control
    // is gone; this guards the whole rendered page against the keys coming back.
    expect(el().innerHTML).not.toContain('DASHBOARD.PERIOD_');
  });
});
