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
    // KAN-98. The window the SERVER applied, echoed back. The heading reads this rather than the
    // picker, so a fixture without it would leave the range chip silently absent.
    from: '2026-09-01',
    to: '2026-09-30',
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
    const req = http.expectOne(r => r.url === url);

    // ★★ THE SECURITY PROPERTY, ASSERTED RATHER THAN ASSUMED. KAN-98 put two values on this request
    // and this is the line that says which two. They name DAYS, never a person: no payeeId, no email,
    // no reference. The endpoint answers about whoever holds the token and nothing here can change that.
    expect(req.request.params.keys().sort()).toEqual(['from', 'to']);
    req.flush(linked());
  });

  /**
   * ★ THE WINDOW IN EFFECT GOES OUT WITH THE REQUEST. A range the store holds but never sends is a
   * picker that moves and changes nothing - and the figures underneath would look perfectly plausible.
   */
  it('sends the window it is holding', () => {
    store.range.set({ from: '2026-07-01', to: '2026-07-31' });
    void store.load();

    const req = http.expectOne(r => r.url === url);
    expect(req.request.params.get('from')).toBe('2026-07-01');
    expect(req.request.params.get('to')).toBe('2026-07-31');
    req.flush(linked());
  });

  /**
   * ★★ A BACKWARDS RANGE IS NOT SENT. The picker orders its own two ends, so this only happens when
   * something else sets the range - but the server refuses it, and a refusal renders as the error state,
   * which on this screen is indistinguishable from "we could not read your pay".
   */
  it('ignores a backwards range instead of asking for one', () => {
    store.range.set({ from: '2026-07-01', to: '2026-07-31' });
    store.setRange({ from: '2026-09-30', to: '2026-09-01' });

    expect(store.range()).toEqual({ from: '2026-07-01', to: '2026-07-31' });
  });

  /**
   * ★★ THE HEADING READS WHAT THE SERVER ANSWERED, NOT WHAT THE PICKER HOLDS. The two differ for the
   * whole of a request, and that gap is exactly when somebody glances at the heading - a label driven by
   * the control would relabel July's pay as August's a full round-trip before the money changed.
   */
  it('reports the window the server applied, not the one requested', async () => {
    store.range.set({ from: '2026-07-01', to: '2026-07-31' });

    const promise = store.load();
    http.expectOne(r => r.url === url).flush(linked({ from: '2026-02-01', to: '2026-04-15' }));
    await promise;

    expect(store.appliedRange()).toEqual({ from: '2026-02-01', to: '2026-04-15' });
  });

  /**
   * ★ AN ALL-TIME ANSWER HAS NO WINDOW TO STATE. The server sends null on both ends when it applied
   * none, and the screen must show no range chip rather than guess one from the picker.
   */
  it('has no applied window when the server applied none', async () => {
    const promise = store.load();
    http.expectOne(r => r.url === url).flush(linked({ from: null, to: null }));
    await promise;

    expect(store.appliedRange()).toBeNull();
  });

  it('exposes the figures the server sent, unchanged', async () => {
    const promise = store.load();
    http.expectOne(r => r.url === url).flush(linked());
    await promise;

    expect(store.linked()).toBe(true);
    expect(store.payeeId()).toBe('p1');
    expect(store.balances()[0].awaitingPaymentAllTime).toBe(400);
    expect(store.quotas().length).toBe(1);
  });

  it('reports "not linked" as a fact, not as an absence of money', async () => {
    const promise = store.load();
    http.expectOne(r => r.url === url).flush(linked({ linked: false, payeeId: null, payeeName: null, summary: null, quotas: [] }));
    await promise;

    expect(store.linked()).toBe(false);
    expect(store.balances()).toEqual([]);
  });

  it('leaves `linked` unknown after a failed request instead of answering false', async () => {
    const promise = store.load();
    http.expectOne(r => r.url === url).flush({ message: 'boom' }, { status: 500, statusText: 'Server Error' });
    await promise;

    // ★★ THE DISTINCTION THIS WHOLE BATCH IS ABOUT. A failed load must not render as "you have no
    // payee" or as zeros: null is "we do not know", and the screen shows the error state for it.
    expect(store.linked()).toBeNull();
    expect(store.error()).toBeTruthy();
  });

  it('drops the previous figures when a reload fails', async () => {
    const first = store.load();
    http.expectOne(r => r.url === url).flush(linked());
    await first;

    const second = store.load();
    http.expectOne(r => r.url === url).flush({}, { status: 500, statusText: 'Server Error' });
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

  /**
   * ★★ KAN-98 - EACH CARD DECLARES ITS OWN WINDOW, and this is the assertion that keeps the four from
   * being labelled as one thing. Earned and Paid move with the picker; Awaiting and You owe cannot,
   * because the ledger has no period dimension at all - "what did they owe in March" is not a question
   * the data can answer. The section used to carry a single "All time" chip over all four, which was
   * true before the range and is now false for half of them. One label meaning two things, on a pay
   * screen, is the shape of every money bug in this repo.
   */
  it('gives each figure the window it actually covers', () => {
    const c = make();
    const scopes = Object.fromEntries(c.cards(BALANCE).map((x) => [x.key, x.scopeKey]));

    expect(scopes['earned']).toBe('MY_DASHBOARD.SCOPE_RANGE');
    expect(scopes['paid']).toBe('MY_DASHBOARD.SCOPE_RANGE');
    expect(scopes['awaiting']).toBe('MY_DASHBOARD.SCOPE_ALL_TIME');
    expect(scopes['debt']).toBe('MY_DASHBOARD.SCOPE_TODAY');
  });
});
