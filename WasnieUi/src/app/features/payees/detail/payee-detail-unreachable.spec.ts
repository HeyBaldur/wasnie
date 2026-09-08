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
import {
  PayeeDashboard,
  PayeeUnreachableCommission,
  UnreachableCommissionGroup,
} from '../models/payee-dashboard.model';

/**
 * The fourth tab: commission the payee is owed that no pay run can reach.
 *
 * ★ WHY THIS FILE EXISTS. A payee showed 385,731.02 EUR as Unpaid and every pay run produced nothing,
 *   because all six assignments were Deactivated. The screen's job is to say WHICH plan and WHAT to
 *   do — so the tests that matter are that the amount arrives and that the reason never degrades into
 *   a raw code (§C2). Signal-level assertions would pass with the markup wired to a dead branch (§A3),
 *   so these go through the DOM.
 */

const EMPTY_PAGE = {
  items: [], totalCount: 0, page: 1, pageSize: 10, totalPages: 0,
  hasNextPage: false, hasPreviousPage: false,
};

function dashboard(): PayeeDashboard {
  return {
    from: '2026-07-01',
    to: '2026-07-31',
    commissionsBand: {
      totalByCurrency: [{ amount: 395840.54, currency: 'EUR' }],
      paidByCurrency: [{ amount: 10109.52, currency: 'EUR' }],
      unpaidByCurrency: [{ amount: 385731.02, currency: 'EUR' }],
      closedTotalByCurrency: [],
      unreachableTotalByCurrency: [{ amount: 385731.02, currency: 'EUR' }],
    },
    attainmentItems: [],
    salesTrend: [],
    recentQuotas: [],
    recentAssignments: [],
  } as PayeeDashboard;
}

function group(over: Partial<UnreachableCommissionGroup> = {}): UnreachableCommissionGroup {
  return {
    planId: 'plan-1',
    planName: 'EU Accelerator',
    planStatus: 'Active',
    creditCount: 12,
    amount: 19481.02,
    currency: 'EUR',
    reason: 'AssignmentDeactivated',
    assignmentId: 'assignment-1',
    effectiveStart: '2026-01-01',
    effectiveEnd: '2026-06-30',
    coversTransactionsFrom: '2026-06-24',
    coversTransactionsTo: '2026-06-24',
    ...over,
  } as UnreachableCommissionGroup;
}

describe('PayeeDetailComponent — unreachable commission tab', () => {
  let fixture: ComponentFixture<PayeeDetailComponent>;
  let component: PayeeDetailComponent;
  let api: jasmine.SpyObj<PayeesApiService>;

  const el = () => fixture.nativeElement as HTMLElement;

  async function settle(): Promise<void> {
    for (let i = 0; i < 5; i++) await Promise.resolve();
    fixture.detectChanges();
  }

  async function build(unreachable: PayeeUnreachableCommission): Promise<void> {
    api = jasmine.createSpyObj<PayeesApiService>('PayeesApiService', [
      'getPayee', 'getPayeeDashboard', 'getPayeeAssignments', 'getPayeeQuotas', 'getPayeeCredits',
      'getPayeeUnreachableCommission',
    ]);
    api.getPayeeAssignments.and.returnValue(of(EMPTY_PAGE) as never);
    api.getPayeeQuotas.and.returnValue(of(EMPTY_PAGE) as never);
    api.getPayeeCredits.and.returnValue(of(EMPTY_PAGE) as never);
    api.getPayeeDashboard.and.returnValue(of(dashboard()) as never);
    api.getPayee.and.returnValue(of(null) as never);
    api.getPayeeUnreachableCommission.and.returnValue(of(unreachable) as never);

    await TestBed.configureTestingModule({
      imports: [PayeeDetailComponent, TranslateModule.forRoot()],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: PayeesApiService, useValue: api },
        {
          provide: PayeesStore,
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
    await settle();
  }

  afterEach(() => TestBed.resetTestingModule());

  it('shows every stuck plan with its amount', async () => {
    await build({
      totalByCurrency: [{ amount: 385731.02, currency: 'EUR' }],
      groups: [
        group({ planId: 'plan-1', planName: 'EU Accelerator', amount: 19481.02 }),
        group({ planId: 'plan-2', planName: 'Core Commission', amount: 366250 }),
      ],
    });

    component.setTab('unreachable');
    await settle();

    const rows = el().querySelectorAll('.pd-unreachable__row');
    expect(rows.length).toBe(2);
    expect(el().textContent).toContain('EU Accelerator');
    expect(el().textContent).toContain('Core Commission');
  });

  /**
   * ★ THE §C2 GUARANTEE. The reason travels as a code; if a newer API sent one this build has never
   *   heard of, the user must get a generic sentence — never the identifier itself.
   */
  it('never prints a raw reason code, not even an unknown one', async () => {
    await build({
      totalByCurrency: [{ amount: 100, currency: 'EUR' }],
      groups: [group({ reason: 'SomethingInventedLater' })],
    });

    component.setTab('unreachable');
    await settle();

    expect(el().textContent).not.toContain('SomethingInventedLater');
    expect(component.unreachableReasonKey('SomethingInventedLater'))
      .toBe('PAYEES.UNREACHABLE.REASON_UNKNOWN');
    expect(component.unreachableFixKey('SomethingInventedLater'))
      .toBe('PAYEES.UNREACHABLE.FIX_UNKNOWN');
  });

  it('maps each known reason to its own sentence', async () => {
    await build({ totalByCurrency: [], groups: [] });

    const keys = ['AssignmentDeactivated', 'NoAssignment', 'PlanArchived']
      .map(r => component.unreachableReasonKey(r));

    expect(new Set(keys).size).toBe(3);
    expect(keys.every(k => k !== 'PAYEES.UNREACHABLE.REASON_UNKNOWN')).toBeTrue();
  });

  it('says nothing is stuck when nothing is', async () => {
    await build({ totalByCurrency: [], groups: [] });

    component.setTab('unreachable');
    await settle();

    expect(el().querySelector('.pd-unreachable__empty')).not.toBeNull();
    expect(el().querySelectorAll('.pd-unreachable__row').length).toBe(0);
  });

  it('loads the stuck money without a date range — the debt is not a month', async () => {
    await build({ totalByCurrency: [], groups: [] });

    component.setTab('unreachable');
    await settle();

    // One argument only: the payee. A window is exactly how this money stayed invisible.
    expect(api.getPayeeUnreachableCommission).toHaveBeenCalledWith('payee-1');
  });
});
