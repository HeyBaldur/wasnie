import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { TranslateModule, TranslateService } from '@ngx-translate/core';
import { PayeeLedgerPanelComponent } from './payee-ledger-panel.component';
import { LedgerStore } from '../state/ledger.store';
import { LEDGER_TRANSACTION_TYPES, PayeeLedgerEntry, PayeeStatement } from '../models/ledger.model';
import { CurrentUserService } from '../../../core/auth/current-user.service';
// The REAL locale file: a test with a hand-written stub would keep passing while the shipped
// translation is missing, which is the failure mode this test exists to catch.
import enTranslations from '../../../../assets/i18n/en.json';

const PAYEE_ID = 'aaaaaaaa-1111-2222-3333-444444444444';

function statement(overrides: Partial<PayeeStatement> = {}): PayeeStatement {
  return {
    payeeId: PAYEE_ID,
    payeeName: 'Ana Sales',
    currency: 'EUR',
    currentBalance: -1650,
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
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        // WsButton inside the form binds routerLink, so the rendered form needs a router.
        provideRouter([]),
        // The adjustment form is hidden without Ledger.Adjust, so without this the DOM tests below
        // would assert against a form that was never rendered — and pass by finding nothing.
        { provide: CurrentUserService, useValue: { hasPermission: () => true } },
      ],
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

  // ── The live balance vs the photograph of a run ─────────────────────────────
  // The confusion this fixes: the header showed the run's carryover (−500) while the ledger summed
  // to −833.33, and nothing on screen said the two described different moments.

  it('reports how much the balance moved after the settled run', () => {
    const st = statement({ currentBalance: -833.33, newCarryover: -500 });

    expect(component.hasMovementsAfterRun(st)).toBeTrue();
    expect(component.movementsAfterRun(st)).toBeCloseTo(-333.33, 2);
  });

  it('says nothing about drift when the run still describes the present', () => {
    const st = statement({ currentBalance: -1650, newCarryover: -1650 });

    expect(component.hasMovementsAfterRun(st)).toBeFalse();
  });

  it('treats a statement with no settled run as having no drift to explain', () => {
    const st = statement({ currentBalance: -800, newCarryover: null, settledAt: null });

    expect(component.hasMovementsAfterRun(st)).toBeFalse();
  });

  it('renders an em dash instead of inventing a zero for a run that does not exist', () => {
    expect(component.fmtAbs(null, 'EUR')).toBe('—');
    expect(component.fmtSigned(null, 'EUR')).toBe('—');
  });

  it('leads with the live balance', () => {
    store.statements.set([statement({ currentBalance: -833.33, newCarryover: -500 })]);
    store.selectCurrency('EUR');
    fixture.detectChanges();
    httpMock.match(() => true).forEach(r => r.flush([]));
    store.loading.set(false);
    fixture.detectChanges();

    const live = fixture.debugElement.query(By.css('.stmt__live-value'));
    expect((live.nativeElement as HTMLElement).textContent).toContain('833.33');
  });

  // ── The sentence under the balance follows its sign ─────────────────────────
  // It was one static line claiming the payee "owes" the figure, which is false the moment the
  // balance is positive — that happens whenever a pay run withheld more than the debt.

  it('says "owes" only when the payee actually owes', () => {
    expect(component.balanceHintKey(-833.33)).toBe('LEDGER.CURRENT_BALANCE_HINT_DEBT');
  });

  it('says the money is owed TO the payee when the balance is positive', () => {
    expect(component.balanceHintKey(500)).toBe('LEDGER.CURRENT_BALANCE_HINT_CREDIT');
  });

  it('says the account is settled at zero', () => {
    expect(component.balanceHintKey(0)).toBe('LEDGER.CURRENT_BALANCE_HINT_SETTLED');
  });

  it('has a label for every ledger transaction type it can be handed', () => {
    // The key is derived from the type name at runtime, so a type added without its translation
    // leaks the raw key onto the screen — which is exactly what DataCorrectionCredit did.
    const types = LEDGER_TRANSACTION_TYPES;
    const translate = TestBed.inject(TranslateService);
    translate.setTranslation('en', enTranslations, true);
    translate.use('en');

    for (const t of types) {
      const key = component.typeLabelKey(t);
      expect(translate.instant(key))
        .withContext(`${t} has no translation: ${key}`)
        .not.toBe(key);
    }
  });

  // ── Closing types follow the sign of the balance ────────────────────────────
  // A departed payee's account closes in the direction the money actually points. Offering both
  // directions would let someone "write off" a balance the company OWES, which is not a write-off.

  function typesFor(balance: number): string[] {
    store.statements.set([statement({ currentBalance: balance })]);
    store.selectCurrency('EUR');
    return component.adjustmentTypes().map(t => t.value);
  }

  it('offers the debt-closing types when the payee owes money', () => {
    const types = typesFor(-500);

    expect(types).toContain('ExternalSettlementCredit');
    expect(types).toContain('WriteOffCredit');
    expect(types).not.toContain('FinalSettlementDebit');
  });

  it('offers the final settlement when the company owes the payee', () => {
    const types = typesFor(500);

    expect(types).toContain('FinalSettlementDebit');
    expect(types).not.toContain('WriteOffCredit');
    expect(types).not.toContain('ExternalSettlementCredit');
  });

  it('offers no closing type at all on a settled account', () => {
    const types = typesFor(0);

    expect(types).not.toContain('FinalSettlementDebit');
    expect(types).not.toContain('WriteOffCredit');
    expect(types).not.toContain('ExternalSettlementCredit');
  });

  it('keeps the general corrections available whatever the balance', () => {
    for (const balance of [-500, 0, 500]) {
      const types = typesFor(balance);
      expect(types).withContext(`balance ${balance}`).toContain('ClawbackForgivenessCredit');
      expect(types).withContext(`balance ${balance}`).toContain('DataCorrectionDebit');
      expect(types).withContext(`balance ${balance}`).toContain('DataCorrectionCredit');
    }
  });

  // ── The amount field follows the rule of the type chosen ────────────────────
  // The three closing types are NOT alike, and the form has to say so. A final settlement must equal
  // the balance exactly (the domain rejects anything else), while a write-off or an external
  // settlement may legitimately cover only part of a debt.

  /** Selects a type on a statement with the given live balance and settles the panel's initial loads. */
  function chooseTypeOn(balance: number, type: string): void {
    store.statements.set([statement({ currentBalance: balance })]);
    store.selectCurrency('EUR');
    fixture.detectChanges();
    httpMock.match(() => true).forEach(r => r.flush([]));
    store.loading.set(false);
    component.form.controls.transactionType.setValue(type as never);
    fixture.detectChanges();
  }

  it('locks the amount to the exact live balance for a final settlement', () => {
    chooseTypeOn(500, 'FinalSettlementDebit');

    expect(component.amountIsLocked()).toBeTrue();
    expect(component.form.controls.amount.disabled).toBeTrue();
    // The exact figure, from the LIVE balance — not a snapshot of any pay run.
    expect(component.form.getRawValue().amount).toBe(500);
  });

  it('renders the locked amount as a non-editable input carrying the balance', () => {
    chooseTypeOn(500, 'FinalSettlementDebit');
    component.showAdjustmentForm.set(true);
    fixture.detectChanges();

    const inputs = fixture.debugElement.queryAll(By.css('input[type="number"]'));
    const amount = inputs[0].nativeElement as HTMLInputElement;
    expect(amount.disabled).toBeTrue();
    expect(amount.value).toBe('500');
  });

  it('still submits the locked amount even though the control is disabled', async () => {
    chooseTypeOn(500, 'FinalSettlementDebit');
    component.form.controls.justification.setValue('Treasury transferred the balance.');

    const submitted = component.submit();
    const req = httpMock.expectOne(`/api/payees/${PAYEE_ID}/ledger/adjustments`);
    expect(req.request.body.amount).toBe(500);
    expect(req.request.body.transactionType).toBe('FinalSettlementDebit');
    req.flush({});

    // The store re-reads the ledger after saving; those GETs only exist once the POST has resolved.
    await new Promise(resolve => setTimeout(resolve));
    httpMock.match(() => true).forEach(r => r.flush([]));
    await submitted;
  });

  it('pre-fills but does NOT lock a write-off — closing part of a debt is legitimate', () => {
    chooseTypeOn(-500, 'WriteOffCredit');

    expect(component.amountIsLocked()).toBeFalse();
    expect(component.amountIsPrefilled()).toBeTrue();
    expect(component.form.controls.amount.enabled).toBeTrue();
    // The debt as a positive magnitude — the sign comes from the type, server-side.
    expect(component.form.getRawValue().amount).toBe(500);
  });

  it('pre-fills but does NOT lock an external settlement either', () => {
    chooseTypeOn(-500, 'ExternalSettlementCredit');

    expect(component.amountIsLocked()).toBeFalse();
    expect(component.form.controls.amount.enabled).toBeTrue();
    expect(component.form.getRawValue().amount).toBe(500);
  });

  it('leaves the amount empty and editable for an ordinary correction', () => {
    chooseTypeOn(-500, 'ManualBonusCredit');

    expect(component.amountIsLocked()).toBeFalse();
    expect(component.amountIsPrefilled()).toBeFalse();
    expect(component.form.controls.amount.enabled).toBeTrue();
    expect(component.form.getRawValue().amount).toBeNull();
  });

  it('releases the lock when the operator switches away from a final settlement', () => {
    chooseTypeOn(500, 'FinalSettlementDebit');
    expect(component.form.controls.amount.disabled).toBeTrue();

    component.form.controls.transactionType.setValue('ManualBonusCredit');

    expect(component.amountIsLocked()).toBeFalse();
    expect(component.form.controls.amount.enabled).toBeTrue();
    expect(component.form.getRawValue().amount).toBeNull();
  });

  it('does not leave the form locked with a stale amount after it is closed', () => {
    chooseTypeOn(500, 'FinalSettlementDebit');
    component.showAdjustmentForm.set(true);

    component.toggleForm();

    expect(component.form.controls.amount.enabled).toBeTrue();
    expect(component.amountIsLocked()).toBeFalse();
  });
});
