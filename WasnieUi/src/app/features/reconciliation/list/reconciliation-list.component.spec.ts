import { ElementRef } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { reasonKey, isKnownReason, UNKNOWN_REASON_KEY } from '../models/reconciliation-reason';
import { ReconciliationStore } from '../state/reconciliation.store';
import { ReconciliationListComponent } from './reconciliation-list.component';
import { ToastService } from '../../../shared/services/toast.service';
import { ReconciliationPage, ReconciliationRow } from '../models/reconciliation.model';

describe('Reconciliation reason whitelist', () => {
  it('maps every known code to its own key', () => {
    expect(reasonKey('NoQuotaInEffect')).toBe('RECONCILIATION.REASON.NO_QUOTA_IN_EFFECT');
    expect(reasonKey('AmountOutsideTable')).toBe('RECONCILIATION.REASON.AMOUNT_OUTSIDE_TABLE');
    expect(reasonKey('NoMatchingBracket')).toBe('RECONCILIATION.REASON.NO_MATCHING_BRACKET');
    expect(reasonKey('NoPayee')).toBe('RECONCILIATION.REASON.NO_PAYEE');
    expect(reasonKey('PlanHasNoActiveRules')).toBe('RECONCILIATION.REASON.PLAN_HAS_NO_ACTIVE_RULES');
  });

  /**
   * ★ KAN-50 arrives as a code the server serves in its reason list, so without an entry here the
   * new bucket would render as the generic phrase — truthfully, but uselessly, since it is the one
   * reason on the screen that means "nothing is wrong with this sale except that it has no money".
   */
  it('names the sale that lacks nothing and still carries no commission', () => {
    expect(reasonKey('ProcessableWithoutCredit'))
      .toBe('RECONCILIATION.REASON.PROCESSABLE_WITHOUT_CREDIT');
    expect(isKnownReason('ProcessableWithoutCredit')).toBe(true);
  });

  /**
   * ★★ THE TEST THIS FILE EXISTS FOR. A key built by concatenation would produce
   * 'RECONCILIATION.REASON.SomethingNobodyTranslated', ngx-translate would fail to resolve it and
   * print the key — an internal identifier on a finance screen. The whitelist cannot do that.
   */
  it('falls back to the generic phrase for a code this build does not know', () => {
    expect(reasonKey('SomeFutureEngineReason')).toBe(UNKNOWN_REASON_KEY);
    expect(reasonKey(null)).toBe(UNKNOWN_REASON_KEY);
    expect(reasonKey('')).toBe(UNKNOWN_REASON_KEY);
  });

  it('never returns anything containing the raw code', () => {
    const code = 'TotallyUnknownCode';
    expect(reasonKey(code)).not.toContain(code);
    expect(isKnownReason(code)).toBe(false);
  });
});

describe('ReconciliationStore', () => {
  let store: ReconciliationStore;
  let http: HttpTestingController;

  const page: ReconciliationPage = {
    items: [
      {
        kind: 'Transaction', entityId: 't1', transactionId: 't1', referenceNumber: 'REF-1',
        payeeId: 'p1', payeeName: 'Ana', payeeCode: 'EMP-1',
        planId: null, planName: null,
        amount: 100, currency: 'EUR', moneyKind: 'AffectedBase',
        periodDate: '2026-03-15', occurredAt: '2026-03-15T00:00:00Z',
        reasons: ['NoPayee'],
      },
    ],
    page: 1,
    pageSize: 25,
    totalCount: 900,
    summary: {
      totalRows: 900,
      byCurrency: [{ currency: 'EUR', affectedBaseAmount: 45_000, clawbackAmount: 800, rowCount: 900 }],
      byReason: [{ reason: 'NoPayee', count: 900 }],
    },
  };

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    store = TestBed.inject(ReconciliationStore);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  /**
   * ★★ THE CARDS ARE THE SERVER'S NUMBERS, NOT A SUM OF THE PAGE. The fixture is deliberately
   * lopsided: one row worth 100 on screen, 45,000 in the summary across 900 entries. A store that
   * derived its totals from `rows()` would report 100 here and would look perfectly plausible.
   */
  it('reports the server summary, not a total derived from the rows on screen', async () => {
    const load = store.load();
    http.expectOne((r) => r.url === '/api/reconciliation').flush(page);
    await load;

    expect(store.rows().length).toBe(1);
    expect(store.summary().byCurrency[0].affectedBaseAmount).toBe(45_000);
    expect(store.summary().totalRows).toBe(900);
    expect(store.total()).toBe(900);
  });

  /** ★ Two figures, and no third: nothing in the model can hold their net. */
  it('keeps clawback separate from the money still owed', async () => {
    const load = store.load();
    http.expectOne((r) => r.url === '/api/reconciliation').flush(page);
    await load;

    const eur = store.summary().byCurrency[0];
    expect(eur.affectedBaseAmount).toBe(45_000);
    expect(eur.clawbackAmount).toBe(800);
    expect(Object.keys(eur)).not.toContain('net');
  });

  /**
   * ★★ THE PAGE SIZE BUTTONS DID NOTHING, AND THE STORE IS WHERE THAT IS PINNED. `ws-pagination`
   * emits `pageSizeChange`; this screen bound only `pageChange`, so 10 / 25 / 50 / 100 rendered,
   * highlighted on click, and went nowhere. The request is asserted rather than the signal, because
   * what was broken was the round trip: a store that set `pageSize` and never asked the server again
   * would look right in state and change nothing on screen.
   */
  it('asks the server for the new page size', async () => {
    const load = store.setPageSize(100);
    const req = http.expectOne((r) => r.url === '/api/reconciliation');

    expect(req.request.params.get('pageSize')).toBe('100');

    req.flush({ ...page, pageSize: 100 });
    await load;
    expect(store.filter().pageSize).toBe(100);
  });

  /**
   * ★★ AND IT RESETS TO PAGE 1. Growing the page size shrinks the number of pages: staying on page 5
   * after switching 10 → 100 asks for a page that no longer exists and the server answers with
   * nothing — an empty table that reads as "no unpaid money", on the screen whose entire job is to
   * say how much there is. Every other list in the app resets; this asserts it does too.
   */
  it('returns to the first page when the page size changes', async () => {
    const first = store.goToPage(5);
    http.expectOne((r) => r.url === '/api/reconciliation').flush({ ...page, page: 5 });
    await first;
    expect(store.filter().page).toBe(5);

    const resize = store.setPageSize(100);
    const req = http.expectOne((r) => r.url === '/api/reconciliation');
    expect(req.request.params.get('page')).toBe('1');

    req.flush({ ...page, page: 1, pageSize: 100 });
    await resize;
    expect(store.filter().page).toBe(1);
  });

  it('sends only the filters that are set', async () => {
    const load = store.load({ reason: 'NoPayee', from: null, to: null });
    const req = http.expectOne((r) => r.url === '/api/reconciliation');

    expect(req.request.params.get('reason')).toBe('NoPayee');
    expect(req.request.params.has('from')).toBe(false);
    expect(req.request.params.has('to')).toBe(false);

    req.flush(page);
    await load;
  });

  it('surfaces a load failure without leaving stale money on screen', async () => {
    const first = store.load();
    http.expectOne((r) => r.url === '/api/reconciliation').flush(page);
    await first;
    expect(store.summary().totalRows).toBe(900);

    const second = store.load();
    http.expectOne((r) => r.url === '/api/reconciliation')
      .flush('boom', { status: 500, statusText: 'Server Error' });
    await second;

    expect(store.error()).toBe('RECONCILIATION.LOAD_ERROR');
    expect(store.rows()).toEqual([]);
    expect(store.summary().totalRows).toBe(0);
    expect(store.summary().byCurrency).toEqual([]);
  });

  /**
   * ★★ A LINK GOES WHERE ITS TEXT SAYS IT GOES. On a Credit row the entity is the CREDIT, while the
   * reference shown is its TRANSACTION's. The first cut linked `['/transactions', entityId]`, which
   * would have carried the reader to a transaction route holding a credit's id — a 404 that looks
   * like a broken record. The row carries the transaction separately for exactly this.
   */
  it('keeps a credit row pointing at the transaction its reference names', async () => {
    const creditPage: ReconciliationPage = {
      ...page,
      items: [{
        ...page.items[0],
        kind: 'Credit',
        entityId: 'credit-1',
        transactionId: 'tx-9',
        referenceNumber: 'REF-TX-9',
      }],
    };

    const load = store.load();
    http.expectOne((r) => r.url === '/api/reconciliation').flush(creditPage);
    await load;

    const row = store.rows()[0];
    expect(row.entityId).toBe('credit-1');
    expect(row.transactionId).toBe('tx-9');
    expect(row.transactionId).not.toBe(row.entityId);
  });

  it('computes the page count from the server total', async () => {
    const load = store.load({ pageSize: 25 });
    http.expectOne((r) => r.url === '/api/reconciliation').flush(page);
    await load;

    expect(store.totalPages()).toBe(36);
  });
});

/**
 * Closing a row by decision (KAN-51).
 *
 * ★ THE STORE IS WHAT IS PINNED, NOT THE MODAL'S MARKUP. What can actually go wrong here is the
 * round trip: a payload that carries the wrong thing, or a screen that hides a row it never got
 * confirmation for. Both are assertions about requests and state, not about a rendered button.
 */
/**
 * ★ THE RELOAD IS ONE MICROTASK BEHIND THE FLUSH. `closeRow` awaits the POST and only THEN calls
 * `load()`, so the GET does not exist at the instant `flush` returns. Yielding to the task queue is
 * what makes the assertion about the real sequence rather than about timing luck.
 */
const settle = () => new Promise((resolve) => setTimeout(resolve, 0));

describe('ReconciliationStore — closing a row', () => {
  let store: ReconciliationStore;
  let http: HttpTestingController;

  const row: ReconciliationRow = {
    kind: 'Transaction', entityId: 't1', transactionId: 't1', referenceNumber: 'REF-1',
    payeeId: 'p1', payeeName: 'Ana', payeeCode: 'EMP-1',
    planId: null, planName: null,
    amount: 100, currency: 'EUR', moneyKind: 'AffectedBase',
    periodDate: '2026-03-15', occurredAt: '2026-03-15T00:00:00Z',
    reasons: ['DealLost'],
  };

  const emptyPage: ReconciliationPage = {
    items: [], page: 1, pageSize: 25, totalCount: 0,
    summary: { totalRows: 0, byCurrency: [], byReason: [] },
  };

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    store = TestBed.inject(ReconciliationStore);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  /**
   * ★★ THE PAYLOAD CARRIES THE ROW AND THE NOTE, AND NOTHING ELSE. No reason codes and no
   * timestamps: the server reads those from its own queue. A client able to state the fact time
   * could close anomalies that have not been detected yet, and the closure is what decides which
   * rows a CFO stops seeing.
   */
  it('sends only the row and the note', async () => {
    const closing = store.closeRow(row, 'Paid before the deal was lost; the clawback is applied.');

    const req = http.expectOne((r) => r.url === '/api/reconciliation/close');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({
      kind: 'Transaction',
      entityId: 't1',
      note: 'Paid before the deal was lost; the clawback is applied.',
    });
    expect(Object.keys(req.request.body)).not.toContain('reasons');
    expect(Object.keys(req.request.body)).not.toContain('factOccurredAt');

    req.flush({ entityId: 't1', kind: 'Transaction', closedReasons: ['DealLost'] });
    await settle();
    http.expectOne((r) => r.url === '/api/reconciliation').flush(emptyPage);
    await closing;

    // ★ Closing also tells the sidebar its count changed — see SidebarBadgesStore.
    await settle();
    http.expectOne('/api/sidebar-badges').flush({ reconciliation: 0, terminatedAccounts: null, financialsTotal: 0 });
  });

  /**
   * ★★ IT RELOADS INSTEAD OF SPLICING THE ROW OUT. Removing the row locally would leave the money
   * cards — computed by the server over the whole filtered set — describing a row the table no
   * longer shows. This screen exists so that a total and a table agree; one request keeps them the
   * same query.
   */
  it('reloads the queue from the server after closing', async () => {
    const closing = store.closeRow(row, 'Reviewed.');
    http.expectOne((r) => r.url === '/api/reconciliation/close')
      .flush({ entityId: 't1', kind: 'Transaction', closedReasons: ['DealLost'] });
    await settle();

    const reload = http.expectOne((r) => r.url === '/api/reconciliation');
    reload.flush({
      ...emptyPage,
      totalCount: 0,
      summary: { totalRows: 0, byCurrency: [], byReason: [] },
    });

    await expectAsync(closing).toBeResolvedTo(true);
    expect(store.rows()).toEqual([]);
    expect(store.summary().totalRows).toBe(0);

    await settle();
    http.expectOne('/api/sidebar-badges').flush({ reconciliation: 0, terminatedAccounts: null, financialsTotal: 0 });
  });

  /**
   * ★★ A FAILED CLOSE HIDES NOTHING. The row must still be on screen and the caller must be told, so
   * the modal can stay open with the note still in it. Reporting success here would tell a person
   * the money was reviewed when the decision was never recorded (§B1).
   */
  it('keeps the row on screen and reports failure when the close is refused', async () => {
    const load = store.load();
    http.expectOne((r) => r.url === '/api/reconciliation').flush({ ...emptyPage, items: [row], totalCount: 1 });
    await load;
    expect(store.rows().length).toBe(1);

    const closing = store.closeRow(row, 'Reviewed.');
    http.expectOne((r) => r.url === '/api/reconciliation/close')
      .flush({ message: 'no longer open' }, { status: 400, statusText: 'Bad Request' });

    // ★ The FAILURE ITSELF is the contract: false, the row still on screen, and not busy any more.
    // What the person is told is a toast raised by the screen, like every other action in this app —
    // the store deliberately keeps no second copy of the message for the two to drift apart.
    await expectAsync(closing).toBeResolvedTo(false);
    expect(store.rows().length).toBe(1);
    expect(store.closing()).toBe(false);
  });
});

/**
 * The toasts (reported in runtime: closing a row succeeded or failed in complete silence).
 *
 * ★ THE COMPONENT IS WHAT IS PINNED, because the toast is the component's job — the store returns a
 * boolean and says nothing. Asserting the ToastService call is asserting the actual contract; a test
 * that only checked the boolean would have passed throughout the bug.
 *
 * ★ THE KEYS ARE ASSERTED, NOT SENTENCES (§C1/§C2). What reaches the container is a translation key,
 * so a missing ES or PL entry is a missing key rather than English leaking onto the screen.
 */
describe('ReconciliationListComponent — closing a row announces both outcomes', () => {
  let component: ReconciliationListComponent;
  let http: HttpTestingController;
  let toast: ToastService;

  const row: ReconciliationRow = {
    kind: 'Transaction', entityId: 't1', transactionId: 't1', referenceNumber: 'REF-1',
    payeeId: 'p1', payeeName: 'Ana', payeeCode: 'EMP-1',
    planId: null, planName: null,
    amount: 100, currency: 'EUR', moneyKind: 'AffectedBase',
    periodDate: '2026-03-15', occurredAt: '2026-03-15T00:00:00Z',
    reasons: ['DealLost'],
  };

  const emptyPage: ReconciliationPage = {
    items: [], page: 1, pageSize: 25, totalCount: 0,
    summary: { totalRows: 0, byCurrency: [], byReason: [] },
  };

  beforeEach(() => {
    TestBed.configureTestingModule({
      // ★ The component reads its host element to scroll the table back to the top; constructing it
      // outside a fixture means supplying that one dependency by hand. Nothing here renders the
      // template — what is under test is the close flow, not the markup.
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: ElementRef, useValue: new ElementRef(document.createElement('div')) },
      ],
    });
    component = TestBed.runInInjectionContext(() => new ReconciliationListComponent());
    http = TestBed.inject(HttpTestingController);
    toast = TestBed.inject(ToastService);
    spyOn(toast, 'show');
  });

  afterEach(() => http.verify());

  it('raises a success toast and closes the modal', async () => {
    component.openClose(row);
    component.closeNote.set('Paid before the deal was lost.');

    const confirming = component.confirmClose();
    http.expectOne((r) => r.url === '/api/reconciliation/close')
      .flush({ entityId: 't1', kind: 'Transaction', closedReasons: ['DealLost'] });
    await settle();
    http.expectOne((r) => r.url === '/api/reconciliation').flush(emptyPage);
    await confirming;

    expect(toast.show).toHaveBeenCalledWith('RECONCILIATION.CLOSE.TOAST_SUCCESS', 'success');
    expect(component.closeTarget()).toBeNull();

    await settle();
    http.expectOne('/api/sidebar-badges').flush({ reconciliation: 0, terminatedAccounts: null, financialsTotal: 0 });
  });

  /** ★ And the modal STAYS OPEN with the note intact, so the person can retry without retyping. */
  it('raises an error toast and keeps the modal open', async () => {
    component.openClose(row);
    component.closeNote.set('Reviewed.');

    const confirming = component.confirmClose();
    http.expectOne((r) => r.url === '/api/reconciliation/close')
      .flush({ message: 'no longer open' }, { status: 400, statusText: 'Bad Request' });
    await confirming;

    expect(toast.show).toHaveBeenCalledWith('RECONCILIATION.CLOSE.TOAST_ERROR', 'error');
    expect(component.closeTarget()).toBe(row);
    expect(component.closeNote()).toBe('Reviewed.');
  });

  /** ★ An empty note never reaches the network: the guard is the button's, and this proves it. */
  it('sends nothing when the note is only whitespace', async () => {
    component.openClose(row);
    component.closeNote.set('   ');

    expect(component.canSubmitClose()).toBe(false);
    await component.confirmClose();
    http.expectNone((r) => r.url === '/api/reconciliation/close');
    expect(toast.show).not.toHaveBeenCalled();
  });
});
