import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { ActivatedRoute, provideRouter } from '@angular/router';
import { TranslateModule } from '@ngx-translate/core';
import { of } from 'rxjs';
import { PayRunDetailComponent } from './pay-run-detail.component';
import { PayRunDetailStore } from '../state/pay-run-detail.store';
import { PayRunDetail } from '../models/pay-run.model';

/**
 * KAN-65 — the payouts table must not blame a filter for a run that holds nothing.
 *
 * `excludeZero` ("Hiding $0 payouts") is ON by default, so the old condition read every run as
 * filtered and answered an empty one with "No payouts match the current filters". That sent a reader
 * hunting through filters for rows that never existed — it cost a full database investigation.
 */

function run(over: Partial<PayRunDetail> = {}): PayRunDetail {
  return {
    id: 'run-1',
    periodStart: '2026-07-01',
    periodEnd: '2026-09-30',
    status: 'Draft',
    supplementalSequence: 0,
    payeeCount: 0,
    paidPayeeCount: 0,
    zeroPayoutCount: 0,
    totalAmounts: {},
    createdAt: '2026-09-08T11:13:00Z',
    createdBy: 'someone',
    approvedAt: null, approvedBy: null, paidAt: null, paidBy: null,
    payouts: {
      items: [], totalCount: 0, page: 1, pageSize: 25, totalPages: 1,
      hasNextPage: false, hasPreviousPage: false, unfilteredTotal: undefined,
    },
    ...over,
  } as PayRunDetail;
}

describe('PayRunDetailComponent — the "nothing here" message', () => {
  let component: PayRunDetailComponent;
  let store: {
    run: () => PayRunDetail | null;
    activeFilterCount: () => number;
    excludeZero: () => boolean;
  };

  function build(
    detail: PayRunDetail | null,
    opts: { filters?: number; excludeZero?: boolean } = {},
  ): void {
    store = {
      run: () => detail,
      activeFilterCount: () => opts.filters ?? 0,
      excludeZero: () => opts.excludeZero ?? true,
    };

    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      imports: [PayRunDetailComponent, TranslateModule.forRoot()],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        {
          provide: ActivatedRoute,
          useValue: {
            snapshot: { paramMap: new Map([['runId', 'run-1']]), queryParamMap: new Map() },
            paramMap: of(new Map()), queryParams: of({}),
          },
        },
      ],
    });

    // ★ The store is a COMPONENT-level provider (`providers: [PayRunDetailStore]`), which wins over
    //   anything the TestBed module supplies — a module-level mock is silently ignored and every test
    //   then reads the real store and fails on the same wrong value.
    TestBed.overrideComponent(PayRunDetailComponent, {
      set: { providers: [{ provide: PayRunDetailStore, useValue: store }] },
    });

    component = TestBed.createComponent(PayRunDetailComponent).componentInstance;
  }

  it('a run that holds nothing says the RUN is empty, not that filters hid rows', () => {
    // ★ THE REPORTED CASE. Zero payouts, and "Hiding $0 payouts" on by default.
    build(run(), { filters: 0, excludeZero: true });

    expect(component.emptyMessageKey()).toBe('PAY_RUNS.DETAIL.EMPTY_TITLE');
  });

  it('still blames the filter when there ARE payouts it could be hiding', () => {
    build(run({ payeeCount: 4 }), { filters: 1, excludeZero: false });

    expect(component.emptyMessageKey()).toBe('PAY_RUNS.DETAIL.EMPTY_FILTER');
  });

  it('names the real cause when every payout in the run is worth nothing', () => {
    // ★ THE REPORTED CASE, SECOND ROUND. The header said "0 paid · 15 total" over an empty table and
    //   the message blamed "the current filters" — leaving the reader to hunt through filters for the
    //   one fact that explains it: all fifteen payouts are zero.
    build(run({ payeeCount: 0, zeroPayoutCount: 15 }), { filters: 0, excludeZero: true });

    expect(component.emptyMessageKey()).toBe('PAY_RUNS.DETAIL.EMPTY_ALL_ZERO');
    expect(component.zeroPayoutCount()).toBe(15);
  });

  it('falls back to the filter message when a real filter is also applied', () => {
    // With a filter of their own on top, "they are all zero" is no longer the whole story.
    build(run({ payeeCount: 0, zeroPayoutCount: 15 }), { filters: 2, excludeZero: true });

    expect(component.emptyMessageKey()).toBe('PAY_RUNS.DETAIL.EMPTY_FILTER');
  });

  it('an empty run with no filters at all still says the run is empty', () => {
    build(run(), { filters: 0, excludeZero: false });

    expect(component.emptyMessageKey()).toBe('PAY_RUNS.DETAIL.EMPTY_TITLE');
  });

  it('does not throw before the run has loaded', () => {
    build(null, { filters: 0, excludeZero: true });

    expect(() => component.emptyMessageKey()).not.toThrow();
    expect(component.emptyMessageKey()).toBe('PAY_RUNS.DETAIL.EMPTY_TITLE');
  });
});
