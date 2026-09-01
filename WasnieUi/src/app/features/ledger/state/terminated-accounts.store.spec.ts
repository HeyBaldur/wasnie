import { TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { TerminatedAccountsStore } from './terminated-accounts.store';
import { LedgerApiService } from '../services/ledger.api.service';
import { TerminatedAccounts, TerminatedPayeeBalance } from '../models/ledger.model';

/**
 * The store over the departed-payee work queue.
 *
 * The rule it exists to keep is negative: WHO belongs in the queue and HOW MUCH is outstanding are
 * both the server's answers. Nothing here re-derives membership and nothing adds money — a total
 * summed in the browser would blend currencies, which Wasnie cannot do because it holds no rates.
 */
describe('TerminatedAccountsStore', () => {
  /**
   * A queue row. `unsettledCreditTotal` drives the credit list so the two can never disagree — a row
   * claiming a total with an empty list is a shape the server does not produce, and a fixture that
   * produced it would test nothing real.
   */
  const row = (over: Partial<TerminatedPayeeBalance> = {}): TerminatedPayeeBalance => {
    const base: TerminatedPayeeBalance = {
      payeeId: 'p1',
      payeeName: 'Birgit Schneider',
      employeeCode: 'DE-101',
      terminationDate: '2026-08-27',
      balance: 0,
      currency: 'EUR',
      balanceUpdatedAt: null,
      accountClosedAt: null,
      unsettledCreditTotal: 0,
      unsettledCredits: [],
      ...over,
    };

    if (base.unsettledCreditTotal > 0 && base.unsettledCredits.length === 0) {
      base.unsettledCredits = [{
        creditId: `credit-${base.payeeId}`,
        amount: base.unsettledCreditTotal,
        currency: base.currency,
        planName: 'EU Accelerator Q2 2026',
        ruleName: 'Tier 1: 4% up to quota',
        allocatedAt: '2026-08-27',
        transactionId: `tx-${base.payeeId}`,
        transactionReference: 'POL-8554',
      }];
    }

    return base;
  };

  function build(queue: TerminatedAccounts): TerminatedAccountsStore {
    const api = { getTerminatedWithBalance: () => of(queue) };
    TestBed.configureTestingModule({
      providers: [TerminatedAccountsStore, { provide: LedgerApiService, useValue: api }],
    });
    return TestBed.inject(TerminatedAccountsStore);
  }

  it('keeps the rows and the totals exactly as the server sent them', async () => {
    const store = build({
      rows: [row({ unsettledCreditTotal: 3869.34 })],
      totals: [{ currency: 'EUR', unsettledCreditTotal: 3869.34, unsettledCreditCount: 1, payeeCount: 1 }],
    });

    await store.load();

    expect(store.count()).toBe(1);
    expect(store.totals()[0].unsettledCreditTotal).toBe(3869.34);
  });

  /**
   * ★ THE CASE THE WHOLE QUEUE WAS REWRITTEN FOR. Commission earned and never paid leaves the ledger
   * balance at exactly zero — the ledger records what someone OWES — so this row lands in neither
   * balance bucket. Without its own count the card would total it and account for it nowhere.
   */
  it('counts unpaid commission separately from both balance buckets', async () => {
    const store = build({
      rows: [
        row({ payeeId: 'owes', balance: -500, balanceUpdatedAt: '2026-08-01T00:00:00Z' }),
        row({ payeeId: 'owed', balance: 250, balanceUpdatedAt: '2026-08-01T00:00:00Z' }),
        row({ payeeId: 'unpaid', balance: 0, unsettledCreditTotal: 3869.34 }),
      ],
      totals: [],
    });

    await store.load();

    expect(store.count()).toBe(3);
    expect(store.owedByPayeesCount()).toBe(1);
    expect(store.owedToPayeesCount()).toBe(1);
    expect(store.unsettledCreditCount()).toBe(1);
  });

  it('clears rows and totals together when the call fails', async () => {
    const api = { getTerminatedWithBalance: () => throwError(() => new Error('boom')) };
    TestBed.configureTestingModule({
      providers: [TerminatedAccountsStore, { provide: LedgerApiService, useValue: api }],
    });
    const store = TestBed.inject(TerminatedAccountsStore);

    await store.load();

    expect(store.error()).toBe('ERRORS.GENERIC');
    expect(store.rows()).toEqual([]);
    expect(store.totals()).toEqual([]);
  });
});
