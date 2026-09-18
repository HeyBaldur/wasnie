import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { TranslateModule } from '@ngx-translate/core';
import { CurrentUserService } from '../../core/auth/current-user.service';
import { MyDashboardComponent } from './my-dashboard.component';
import { MyDashboardStore } from './state/my-dashboard.store';
import { MyCurrencyBalance, MyDashboard, MyQuotaAttainment } from './models/my-dashboard.model';
import { environment } from '../../../environments/environment';

/**
 * KAN-92 batch 3.
 *
 * ★ THE FIXTURES ARE THE SHAPE THE API REALLY SENDS (section A4): enums arrive as STRINGS, because
 * Program.cs registers JsonStringEnumConverter. A fixture with numbers here would compare false
 * forever and pass while doing so.
 */
const BALANCE: MyCurrencyBalance = {
  currency: 'EUR',
  earnedCommissionsInPeriod: 1200,
  paidOutInPeriod: 800,
  disputedInPeriod: 0,
  awaitingPaymentAllTime: 400,
  outstandingDebt: 0,
  netPendingPayout: 400,
  interpretation: 'EarningsAndNoDebt',
  clawbackCreditAllTime: 0,
};

const MEASURED_QUOTA: MyQuotaAttainment = {
  quotaId: 'q1',
  planName: 'Plan A',
  targetAmount: 10000,
  currency: 'EUR',
  measurement: 'Revenue',
  achievedAmount: 7600,
  attainmentRatio: 0.76,
  attainmentSource: 'Measured',
  periodStart: '2026-01-01',
  periodEnd: '2026-12-31',
};

function linked(overrides: Partial<MyDashboard> = {}): MyDashboard {
  return {
    linked: true,
    payeeId: 'p1',
    payeeName: 'Ada Lovelace',
    summary: {
      payeeId: 'p1',
      payeeName: 'Ada Lovelace',
      periodLabel: 'all-time',
      periodStart: null,
      periodEnd: null,
      byCurrency: [BALANCE],
    },
    quotas: [MEASURED_QUOTA],
    // KAN-94. Nothing stuck by default: these specs are about the money figures, and a notice about
    // unpayable sales sitting over them would be noise in every one of them.
    salesAwaitingSetup: 0,
    ...overrides,
  };
}

describe('MyDashboardStore', () => {
  let store: MyDashboardStore;
  let http: HttpTestingController;

  const url = `${environment.apiBaseUrl}/me/dashboard`;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    store = TestBed.inject(MyDashboardStore);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('asks for the caller own dashboard and sends no identifier with it', () => {
    void store.load();
    const req = http.expectOne(url);

    // ★ The security property, asserted rather than assumed: nothing in this request names a payee.
    expect(req.request.params.keys().length).toBe(0);
    req.flush(linked());
  });

  it('exposes the figures the server sent, unchanged', async () => {
    const promise = store.load();
    http.expectOne(url).flush(linked());
    await promise;

    expect(store.linked()).toBe(true);
    expect(store.payeeId()).toBe('p1');
    expect(store.balances()[0].awaitingPaymentAllTime).toBe(400);
    expect(store.quotas().length).toBe(1);
  });

  it('reports "not linked" as a fact, not as an absence of money', async () => {
    const promise = store.load();
    http.expectOne(url).flush(linked({ linked: false, payeeId: null, payeeName: null, summary: null, quotas: [] }));
    await promise;

    expect(store.linked()).toBe(false);
    expect(store.balances()).toEqual([]);
  });

  it('leaves `linked` unknown after a failed request instead of answering false', async () => {
    const promise = store.load();
    http.expectOne(url).flush({ message: 'boom' }, { status: 500, statusText: 'Server Error' });
    await promise;

    // ★★ THE DISTINCTION THIS WHOLE BATCH IS ABOUT. A failed load must not render as "you have no
    // payee" or as zeros: null is "we do not know", and the screen shows the error state for it.
    expect(store.linked()).toBeNull();
    expect(store.error()).toBeTruthy();
  });

  it('drops the previous figures when a reload fails', async () => {
    const first = store.load();
    http.expectOne(url).flush(linked());
    await first;

    const second = store.load();
    http.expectOne(url).flush({}, { status: 500, statusText: 'Server Error' });
    await second;

    expect(store.dashboard()).toBeNull();
  });
});

describe('MyDashboardComponent', () => {
  function make() {
    TestBed.configureTestingModule({
      imports: [MyDashboardComponent, TranslateModule.forRoot()],
      providers: [
        provideHttpClient(), provideHttpClientTesting(), provideRouter([]),
        { provide: CurrentUserService, useValue: { hasPermission: () => false } },
      ],
    });
    return TestBed.createComponent(MyDashboardComponent).componentInstance;
  }

  it('maps every balance token to its own sentence', () => {
    const c = make();
    const keys = (['NothingRecorded', 'EarningsAndNoDebt', 'EarningsWithDebt', 'DebtOnly', 'DebtExceedsPending'] as const)
      .map((t) => c.interpretationKey({ ...BALANCE, interpretation: t }));

    expect(new Set(keys).size).toBe(5);
    expect(keys.some((k) => k.includes('undefined'))).toBe(false);
  });

  it('falls back to a generic line for a token it does not know, never printing the token', () => {
    const c = make();
    // A member added on the server before this screen learns about it.
    const key = c.interpretationKey({ ...BALANCE, interpretation: 'SomethingNew' as never });

    expect(key).toBe('MY_DASHBOARD.STATE_UNKNOWN');
  });

  it('treats a NoTarget quota as having no target rather than as 0%', () => {
    const c = make();

    expect(c.hasTarget(MEASURED_QUOTA)).toBe(true);
    expect(c.hasTarget({ ...MEASURED_QUOTA, attainmentSource: 'NoTarget', attainmentRatio: 0 })).toBe(false);
  });

  it('knows a Units target is not money', () => {
    const c = make();

    expect(c.isUnits({ ...MEASURED_QUOTA, measurement: 'Units' })).toBe(true);
    expect(c.isUnits(MEASURED_QUOTA)).toBe(false);
  });

  it('labels the two all-time figures apart from the two period ones', () => {
    const c = make();
    const keys = c.cards(BALANCE).map((x) => x.key);

    expect(keys).toEqual(['earned', 'paid', 'awaiting', 'debt']);
  });
});
