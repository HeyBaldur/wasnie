import { TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { ActivatedRoute, Router } from '@angular/router';
import { PayRunDetailComponent } from './pay-run-detail.component';
import { PayRunDetailStore } from '../state/pay-run-detail.store';
import { PayRunsApiService } from '../services/pay-runs.api.service';
import { PayeesApiService } from '../../payees/services/payees.api.service';
import { PlansApiService } from '../../plans/services/plans.api.service';
import { CreditsApiService } from '../../credits/services/credits.api.service';
import { EMPTY_PAYOUTS_DETAIL_FILTER, PayRunDetail } from '../models/pay-run.model';
import { PayoutListItem } from '../../payouts/models/payout.model';

const makePagedPayouts = (items: PayoutListItem[] = []) => ({
  items, totalCount: 0, page: 1, pageSize: 25, totalPages: 1,
  hasNextPage: false, hasPreviousPage: false, unfilteredTotal: undefined,
});

const makeRun = (overrides: Partial<PayRunDetail> = {}): PayRunDetail => ({
  id: 'run-1',
  periodStart: '2026-04-01',
  periodEnd: '2026-06-30',
  status: 'Draft',
  payeeCount: 6,
  paidPayeeCount: 0,
  zeroPayoutCount: 0,
  totalAmounts: { EUR: 11000 },
  createdAt: '2026-06-01T00:00:00Z',
  createdBy: 'admin',
  approvedAt: null,
  approvedBy: null,
  paidAt: null,
  paidBy: null,
  payouts: makePagedPayouts(),
  ...overrides,
});

describe('PayRunDetailComponent — recalculate credits', () => {
  let component: PayRunDetailComponent;
  let creditsApiSpy: jasmine.SpyObj<CreditsApiService>;
  let storeSpy: jasmine.SpyObj<PayRunDetailStore>;

  beforeEach(() => {
    creditsApiSpy = jasmine.createSpyObj<CreditsApiService>('CreditsApiService', [
      'recalculate', 'list', 'counters', 'byPayee', 'getById', 'exportToExcel',
    ]);

    storeSpy = jasmine.createSpyObj<PayRunDetailStore>(
      'PayRunDetailStore',
      ['load', 'reload', 'setFilter', 'clearFilters', 'setPage', 'setPageSize', 'setExcludeZero', 'toExportParams',
       'run', 'loading', 'error', 'isDraft', 'isApproved', 'isPaid', 'status',
       'payoutItems', 'payoutTotalCount', 'payoutTotalPages', 'pageSize', 'page',
       'excludeZero', 'activeFilterCount', 'totalAmountsEntries', 'filter', 'markPaidSummary'],
    );
    (storeSpy.run as jasmine.Spy).and.returnValue(makeRun());
    (storeSpy.loading as jasmine.Spy).and.returnValue(false);
    (storeSpy.error as jasmine.Spy).and.returnValue(null);
    (storeSpy.isDraft as jasmine.Spy).and.returnValue(true);
    (storeSpy.isApproved as jasmine.Spy).and.returnValue(false);
    (storeSpy.isPaid as jasmine.Spy).and.returnValue(false);
    (storeSpy.status as jasmine.Spy).and.returnValue('Draft');
    (storeSpy.payoutItems as jasmine.Spy).and.returnValue([]);
    (storeSpy.payoutTotalCount as jasmine.Spy).and.returnValue(0);
    (storeSpy.payoutTotalPages as jasmine.Spy).and.returnValue(1);
    (storeSpy.pageSize as jasmine.Spy).and.returnValue(25);
    (storeSpy.page as jasmine.Spy).and.returnValue(1);
    (storeSpy.excludeZero as jasmine.Spy).and.returnValue(true);
    (storeSpy.activeFilterCount as jasmine.Spy).and.returnValue(0);
    (storeSpy.totalAmountsEntries as jasmine.Spy).and.returnValue([]);
    (storeSpy.filter as jasmine.Spy).and.returnValue({ ...EMPTY_PAYOUTS_DETAIL_FILTER });
    (storeSpy.markPaidSummary as jasmine.Spy).and.returnValue({ count: 0, totalAmounts: [], skippedCount: 0 });
    storeSpy.reload.and.returnValue(Promise.resolve());

    TestBed.configureTestingModule({
      imports: [PayRunDetailComponent],
      providers: [
        { provide: PayRunDetailStore, useValue: storeSpy },
        { provide: CreditsApiService, useValue: creditsApiSpy },
        { provide: PayRunsApiService, useValue: jasmine.createSpyObj('PayRunsApiService', ['getById', 'getOverlaps', 'exportRunPayouts', 'approve', 'markPaid', 'reopen', 'deleteDraft']) },
        { provide: PayeesApiService, useValue: jasmine.createSpyObj('PayeesApiService', ['getPayees']) },
        { provide: PlansApiService, useValue: jasmine.createSpyObj('PlansApiService', ['getPlans']) },
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: { get: () => 'run-1' } } } },
        { provide: Router, useValue: jasmine.createSpyObj('Router', ['navigate']) },
      ],
    });

    TestBed.overrideComponent(PayRunDetailComponent, {
      set: { template: '<div></div>', imports: [], providers: [] },
    });

    const fixture = TestBed.createComponent(PayRunDetailComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should close modal, set recalculateResult, and reload on success', async () => {
    creditsApiSpy.recalculate.and.returnValue(of({ supersededCount: 6, skippedPaidCount: 0, jobIds: ['job-1'] }));

    component.recalculateConfirmOpen.set(true);
    await component.onRecalculate();

    expect(creditsApiSpy.recalculate).toHaveBeenCalledWith('2026-04-01', '2026-06-30');
    expect(component.recalculateResult()).toEqual({ supersededCount: 6, jobCount: 1 });
    expect(component.recalculateError()).toBeNull();
    expect(storeSpy.reload).toHaveBeenCalledTimes(1);
    expect(component.recalculateConfirmOpen()).toBeFalse();
    expect(component.recalculating()).toBeFalse();
  });

  it('should set recalculateError and not reload on API failure', async () => {
    creditsApiSpy.recalculate.and.returnValue(throwError(() => ({ error: { message: 'SERVER_ERROR' } })));

    await component.onRecalculate();

    expect(component.recalculateError()).toBe('SERVER_ERROR');
    expect(component.recalculateResult()).toBeNull();
    expect(storeSpy.reload).not.toHaveBeenCalled();
    expect(component.recalculating()).toBeFalse();
  });

  it('should not call API when already recalculating (idempotency guard)', async () => {
    component.recalculating.set(true);
    await component.onRecalculate();

    expect(creditsApiSpy.recalculate).not.toHaveBeenCalled();
  });
});
