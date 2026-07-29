import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TranslateModule } from '@ngx-translate/core';
import { PayeeLedgerPanelComponent } from './payee-ledger-panel.component';
import { LedgerStore } from '../state/ledger.store';
import { PayeeLedgerEntry, PayeeStatement } from '../models/ledger.model';

const PAYEE_ID = 'aaaaaaaa-1111-2222-3333-444444444444';

function statement(overrides: Partial<PayeeStatement> = {}): PayeeStatement {
  return {
    payeeId: PAYEE_ID,
    payeeName: 'Ana Sales',
    currency: 'EUR',
    commissionsThisPeriod: 1000,
    retentionApplied: 500,
    netPayable: 500,
    previousDebt: -2150,
    amortization: 500,
    newCarryover: -1650,
    capPercentApplied: 50,
    capLimited: true,
    payRunId: 'run-1',
    settledAt: '2026-07-01T00:00:00Z',
    ...overrides,
  };
}

function entry(overrides: Partial<PayeeLedgerEntry> = {}): PayeeLedgerEntry {
  return {
    id: 'e1',
    createdAt: '2026-07-01T00:00:00Z',
    origin: 'System',
    transactionType: 'ClawbackDebit',
    amount: -800,
    currency: 'EUR',
    justification: 'Deal churned inside maturation.',
    createdBy: 'system',
    sourceExternalDealId: '512147967174',
    sourceTransactionId: null,
    daysActive: 30,
    maturationDays: 90,
    sourceCommissionAmount: 1200,
    eventDate: '2026-05-02',
    sourcePlanId: 'plan-1',
    ...overrides,
  };
}

describe('PayeeLedgerPanelComponent', () => {
  let fixture: ComponentFixture<PayeeLedgerPanelComponent>;
  let component: PayeeLedgerPanelComponent;
  let store: LedgerStore;
  let httpMock: HttpTestingController;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [PayeeLedgerPanelComponent, TranslateModule.forRoot()],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();

    fixture = TestBed.createComponent(PayeeLedgerPanelComponent);
    component = fixture.componentInstance;
    store = TestBed.inject(LedgerStore);
    httpMock = TestBed.inject(HttpTestingController);
    fixture.componentRef.setInput('payeeId', PAYEE_ID);
    store.reset();
  });

  afterEach(() => httpMock.verify({ ignoreCancelled: true }));

  it('shows the statement figures exactly as the server sent them', () => {
    store.statements.set([statement()]);
    store.selectCurrency('EUR');

    const st = store.activeStatement()!;
    // The panel must not derive one figure from another: the DTO is the single source of truth.
    expect(st.netPayable).toBe(500);
    expect(st.retentionApplied).toBe(500);
    expect(st.amortization).toBe(500);
    expect(st.newCarryover).toBe(-1650);
  });

  it('formats without changing the number', () => {
    const formatted = component.fmtAbs(-1650, 'EUR');
    expect(formatted).toContain('1,650');
  });

  it('prints an explicit sign for balance figures', () => {
    expect(component.fmtSigned(-1650, 'EUR')).toContain('−');
    expect(component.fmtSigned(500, 'EUR')).toContain('+');
  });

  it('separates System from Human entries', () => {
    expect(component.isSystem('System')).toBe(true);
    expect(component.isSystem('Human')).toBe(false);
  });

  it('treats a positive amount as a credit and a negative one as a debit', () => {
    expect(component.isCredit(500)).toBe(true);
    expect(component.isCredit(-800)).toBe(false);
  });

  it('maps a transaction type to its translation key', () => {
    expect(component.typeLabelKey('ClawbackForgivenessCredit'))
      .toBe('LEDGER.TYPE_CLAWBACK_FORGIVENESS_CREDIT');
    expect(component.typeLabelKey('ClawbackAppliedCredit'))
      .toBe('LEDGER.TYPE_CLAWBACK_APPLIED_CREDIT');
  });

  it('refuses to submit an adjustment without a justification', async () => {
    store.statements.set([statement()]);
    component.form.setValue({
      transactionType: 'ClawbackForgivenessCredit',
      amount: 100,
      justification: '',
    });

    await component.submit();

    expect(component.form.invalid).toBe(true);
    httpMock.expectNone(`/api/payees/${PAYEE_ID}/ledger/adjustments`);
  });

  it('refuses to submit a zero amount', async () => {
    store.statements.set([statement()]);
    component.form.setValue({
      transactionType: 'ManualBonusCredit',
      amount: 0,
      justification: 'reason',
    });

    await component.submit();

    expect(component.form.invalid).toBe(true);
    httpMock.expectNone(`/api/payees/${PAYEE_ID}/ledger/adjustments`);
  });

  it('keeps the entries the server returned, newest first, without recomputing a balance', () => {
    const rows = [
      entry({ id: 'e2', transactionType: 'ClawbackForgivenessCredit', origin: 'Human', amount: 600 }),
      entry(),
    ];
    store.entries.set(rows);

    expect(store.entries().length).toBe(2);
    expect(store.entries()[0].id).toBe('e2');
    // Append-only: the original clawback is still there after a forgiveness.
    expect(store.entries().some((e) => e.transactionType === 'ClawbackDebit')).toBe(true);
  });

  it('exposes one statement per currency and never sums across them', () => {
    store.statements.set([
      statement(),
      statement({ currency: 'USD', netPayable: 300, commissionsThisPeriod: 300 }),
    ]);
    store.selectCurrency('USD');

    expect(store.currencies()).toEqual(['EUR', 'USD']);
    expect(store.activeStatement()!.currency).toBe('USD');
    expect(store.activeStatement()!.netPayable).toBe(300);
  });

  // ── The loss date is a column, not a sentence ────────────────────────────────
  // It used to live only inside the justification text, so reading "when did this deal actually die?"
  // meant parsing English prose. It is a typed field now and the table renders it on its own.

  /** Renders the panel and settles the two loads it fires on init, so the DOM can be inspected. */
  function render(entries: PayeeLedgerEntry[]): string[] {
    fixture.detectChanges();
    httpMock.match(() => true).forEach(r => r.flush([]));
    store.entries.set(entries);
    store.loading.set(false);
    fixture.detectChanges();

    return fixture.debugElement.queryAll(By.css('tbody td'))
      .map(c => (c.nativeElement as HTMLElement).textContent?.trim() ?? '');
  }

  it('renders the CRM loss date in its own cell, separate from the booking date', () => {
    const cells = render([entry({ createdAt: '2026-07-29T00:00:00Z', eventDate: '2026-05-02' })]);

    // Two different dates, both visible: booked in July, the deal died in May.
    expect(cells.some(t => t.includes('Jul') && t.includes('2026'))).toBeTrue();
    expect(cells.some(t => t.includes('May') && t.includes('2026'))).toBeTrue();
  });

  it('shows a dash when the entry came from no CRM event', () => {
    const cells = render([entry({ eventDate: null })]);
    expect(cells.some(t => t === '—')).toBeTrue();
  });
});
