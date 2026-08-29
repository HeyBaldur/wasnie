import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { provideHttpClient } from '@angular/common/http';
import { HttpErrorResponse } from '@angular/common/http';
import { of, throwError } from 'rxjs';
import { TranslateModule } from '@ngx-translate/core';
import { TerminatedAccountsComponent } from './terminated-accounts.component';
import { LedgerApiService } from '../services/ledger.api.service';
import { TerminatedPayeeBalance } from '../models/ledger.model';
import en from '../../../../assets/i18n/en.json';
import es from '../../../../assets/i18n/es.json';
import pl from '../../../../assets/i18n/pl.json';

/**
 * The closure modal, which is the last thing between a person and an irreversible decision.
 *
 * ★★ WHAT THESE PIN. Confirming here can end a claim a departed person still had: the credits reach a
 * terminal state and the ledger entry is permanent. So two properties matter more than the rest — the
 * request carries EXACTLY the ids and amounts that were displayed, and a 409 is never retried in
 * silence (docs/DIAG_ORPHAN_ACCOUNT_CLOSURE.md).
 */
describe('TerminatedAccountsComponent — closing an account', () => {
  let component: TerminatedAccountsComponent;
  let api: jasmine.SpyObj<LedgerApiService>;

  const credit = (id: string, amount: number) => ({
    creditId: id,
    amount,
    currency: 'EUR',
    planName: 'EU Accelerator Q2 2026',
    ruleName: 'Tier 1: 4% up to quota',
    allocatedAt: '2026-08-27',
    transactionId: `tx-${id}`,
    transactionReference: `POL-${id}`,
  });

  const row = (over: Partial<TerminatedPayeeBalance> = {}): TerminatedPayeeBalance => ({
    payeeId: 'birgit',
    payeeName: 'Birgit Schneider',
    employeeCode: 'DE-101',
    terminationDate: '2026-08-27',
    balance: 0,
    currency: 'EUR',
    balanceUpdatedAt: null,
    accountClosedAt: null,
    unsettledCreditTotal: 3869.34,
    unsettledCredits: [credit('8554', 3869.34)],
    ...over,
  });

  beforeEach(async () => {
    api = jasmine.createSpyObj<LedgerApiService>('LedgerApiService', [
      'getTerminatedWithBalance',
      'closeAccount',
    ]);
    api.getTerminatedWithBalance.and.returnValue(of({ rows: [], totals: [] }));
    api.closeAccount.and.returnValue(of({
      creditsClosed: 1, creditTotalClosed: 3869.34, balanceBefore: 0, balanceAfter: 0, currency: 'EUR',
    }));

    await TestBed.configureTestingModule({
      imports: [TerminatedAccountsComponent, TranslateModule.forRoot()],
      providers: [provideRouter([]), provideHttpClient(), { provide: LedgerApiService, useValue: api }],
    }).compileComponents();

    component = TestBed.createComponent(TerminatedAccountsComponent).componentInstance;
  });

  // ── The payload ────────────────────────────────────────────────────────────

  /**
   * ★★ THE IDS AND AMOUNTS EXACTLY AS DISPLAYED. The server closes this set or refuses with 409; a
   * client that sent a total, or re-derived the set at confirm time, would close something the user
   * never looked at.
   */
  it('sends the exact credits it displayed', async () => {
    component.openClose(row());
    component.noteControl.setValue('Settled with the final paycheck.');

    await component.confirmClose();

    const [payeeId, request] = api.closeAccount.calls.mostRecent().args;
    expect(payeeId).toBe('birgit');
    expect(request.credits).toEqual([{ creditId: '8554', amount: 3869.34 }]);
    expect(request.currency).toBe('EUR');
    expect(request.note).toBe('Settled with the final paycheck.');
  });

  /**
   * ★ "No balance row" is not "a balance of zero". The server compares the two differently, so the
   * client must send null rather than coerce it.
   */
  it('sends a null balance when the row had no ledger balance at all', async () => {
    component.openClose(row({ balance: 0, balanceUpdatedAt: null }));
    component.noteControl.setValue('note');

    await component.confirmClose();

    expect(api.closeAccount.calls.mostRecent().args[1].expectedBalance).toBeNull();
  });

  it('sends the balance it displayed when there was one', async () => {
    component.openClose(row({ balance: -800, balanceUpdatedAt: '2026-08-01T00:00:00Z' }));
    component.noteControl.setValue('note');

    await component.confirmClose();

    expect(api.closeAccount.calls.mostRecent().args[1].expectedBalance).toBe(-800);
  });

  it('defaults to settling externally, and reports a write-off as the destructive one', () => {
    component.openClose(row());
    expect(component.resolutionControl.value).toBe('SettledExternally');
    expect(component.isWriteOff()).toBeFalse();

    component.resolutionControl.setValue('WrittenOff');
    expect(component.isWriteOff()).toBeTrue();
  });

  // ── The note ───────────────────────────────────────────────────────────────

  /**
   * The reason is stored on every credit that is closed, so it is not optional politeness — it is the
   * only sentence a future auditor gets about why this money ended.
   */
  it('refuses to submit without a reason', async () => {
    component.openClose(row());
    component.noteControl.setValue('');

    await component.confirmClose();

    expect(api.closeAccount).not.toHaveBeenCalled();
    expect(component.closeTarget()).withContext('the modal stays open').not.toBeNull();
  });

  // ── ★ The 409 ──────────────────────────────────────────────────────────────

  /**
   * ★★ NEVER RETRIED IN SILENCE. A conflict means the account is not the one the user was shown.
   * Repeating the body would close a set nobody ever saw — the exact outcome the strict set check
   * exists to prevent — so the screen explains, reloads, and makes them look again.
   */
  it('explains a 409, reloads, and leaves the modal blocked', async () => {
    api.closeAccount.and.returnValue(throwError(() => new HttpErrorResponse({
      status: 409,
      error: { error: 'AccountSnapshotStale', reason: 'CreditAppeared' },
    })));

    component.openClose(row());
    component.noteControl.setValue('note');
    api.getTerminatedWithBalance.calls.reset();

    await component.confirmClose();

    expect(component.closeConflict()).toBe('LEDGER.CLOSE_CONFLICT_CreditAppeared');
    expect(api.getTerminatedWithBalance)
      .withContext('the list is reloaded so the user sees what is actually there').toHaveBeenCalled();
    expect(component.closeTarget())
      .withContext('the modal stays open carrying the explanation').not.toBeNull();
  });

  /**
   * ★ An unrecognised reason degrades to a neutral sentence. Building the key by concatenation would
   * print a raw backend identifier the first time the server ships a code ahead of the translation.
   */
  it('degrades an unknown conflict reason instead of printing an identifier', async () => {
    api.closeAccount.and.returnValue(throwError(() => new HttpErrorResponse({
      status: 409,
      error: { reason: 'SomethingNewOnTheServer' },
    })));

    component.openClose(row());
    component.noteControl.setValue('note');

    await component.confirmClose();

    expect(component.closeConflict()).toBe('LEDGER.CLOSE_CONFLICT_UNKNOWN');
  });

  it('treats a non-409 failure as an ordinary error, not a conflict', async () => {
    api.closeAccount.and.returnValue(throwError(() => new HttpErrorResponse({
      status: 400,
      error: { message: 'Only a terminated payee can be closed.' },
    })));

    component.openClose(row());
    component.noteControl.setValue('note');

    await component.confirmClose();

    expect(component.closeConflict()).toBeNull();
    expect(component.closeError()).toBe('Only a terminated payee can be closed.');
  });

  // ── After a success ────────────────────────────────────────────────────────

  it('closes the modal and reloads the queue after a successful closure', async () => {
    component.openClose(row());
    component.noteControl.setValue('note');
    api.getTerminatedWithBalance.calls.reset();

    await component.confirmClose();

    expect(component.closeTarget()).toBeNull();
    expect(api.getTerminatedWithBalance).toHaveBeenCalled();
  });
});

// ══ The words, in three languages ════════════════════════════════════════════

describe('Closing an account — EN / ES / PL', () => {
  const bundles: Record<string, any> = { en, es, pl };

  const keys = [
    'CLOSE_ACTION', 'CLOSE_TITLE', 'CLOSE_INTRO',
    'CLOSE_FIGURE_UNSETTLED', 'CLOSE_FIGURE_CREDITS', 'CLOSE_FIGURE_BALANCE',
    'CLOSE_RESOLUTION_LABEL', 'CLOSE_RESOLUTION_SETTLED', 'CLOSE_RESOLUTION_WRITTEN_OFF',
    'CLOSE_RESOLUTION_SETTLED_HINT', 'CLOSE_RESOLUTION_WRITTEN_OFF_HINT',
    'CLOSE_NOTE_LABEL', 'CLOSE_NOTE_PLACEHOLDER', 'CLOSE_NOTE_REQUIRED',
    'CLOSE_WARNING', 'CLOSE_WARNING_WRITTEN_OFF', 'CLOSE_CONFIRM',
    'CLOSE_CONFLICT_CreditAppeared', 'CLOSE_CONFLICT_CreditDisappeared',
    'CLOSE_CONFLICT_CreditAmountChanged', 'CLOSE_CONFLICT_BalanceChanged',
    'CLOSE_CONFLICT_UNKNOWN', 'CLOSE_CONFLICT_RELOADED',
    'REOPENED', 'REOPENED_HINT',
  ];

  for (const lang of ['en', 'es', 'pl']) {
    it(`has every closure string in ${lang}`, () => {
      for (const key of keys) {
        expect(bundles[lang]['LEDGER'][key]).withContext(`${lang}: LEDGER.${key}`).toBeTruthy();
      }
    });
  }

  /**
   * ★ THE IRREVERSIBILITY IS SAID OUT LOUD, IN EVERY LANGUAGE. The ceremony is the point; a warning
   * that exists only in English is a warning most of this product's users never receive.
   */
  it('warns that a write-off cannot be undone, in every language', () => {
    for (const lang of ['en', 'es', 'pl']) {
      expect(bundles[lang]['LEDGER']['CLOSE_WARNING_WRITTEN_OFF'].length)
        .withContext(`${lang}: the severe warning must actually say something`)
        .toBeGreaterThan(bundles[lang]['LEDGER']['CLOSE_WARNING'].length - 1);
    }
  });
});
