import { computed, inject, Injectable, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { LedgerApiService } from '../services/ledger.api.service';
import { TerminatedAccountsTotal, TerminatedPayeeBalance } from '../models/ledger.model';

/**
 * The queue of departed payees whose account is still open, shared by the screen that lists them and
 * the dashboard card that announces them.
 *
 * WHO decides what belongs in the queue: the SERVER. `GET /ledger/terminated-with-balance` returns
 * exactly the payees who are terminated with something still open — a non-zero ledger balance, unpaid
 * commission, or both — so nothing here re-derives that rule. The counts below are `length` and
 * `filter` over the rows the backend chose.
 *
 * ★ NO MONEY IS ADDED IN THIS FILE. The totals arrive computed and per currency; the store passes them
 * through untouched. Summing `unsettledCreditTotal` across rows here would blend currencies, which is
 * the one thing Wasnie cannot do — it holds no exchange rates.
 */
@Injectable({ providedIn: 'root' })
export class TerminatedAccountsStore {
  private readonly api = inject(LedgerApiService);

  readonly rows = signal<TerminatedPayeeBalance[]>([]);
  readonly totals = signal<TerminatedAccountsTotal[]>([]);
  readonly loading = signal(false);
  readonly error = signal<string | null>(null);

  /** Every open account, whichever way the money points and whichever half put it on the list. */
  readonly count = computed(() => this.rows().length);

  /** Positive balance: the company still OWES them — a liability nobody is processing any more. */
  readonly owedToPayeesCount = computed(() => this.rows().filter((r) => r.balance > 0).length);

  /** Negative balance: THEY owe the company — debt to recover or write off. */
  readonly owedByPayeesCount = computed(() => this.rows().filter((r) => r.balance < 0).length);

  /**
   * Rows carrying commission that was earned and never paid.
   *
   * ★ This is a THIRD count, not a slice of the two above. A payee whose only open item is unpaid
   * commission has a ledger balance of exactly zero — the ledger records what someone OWES, and
   * nobody owes anything here — so they land in neither of the balance buckets. Counting them
   * separately is what keeps the card's numbers adding up to what the queue actually holds.
   */
  readonly unsettledCreditCount = computed(
    () => this.rows().filter((r) => r.unsettledCredits.length > 0).length,
  );

  async load(): Promise<void> {
    this.loading.set(true);
    this.error.set(null);
    try {
      const queue = await firstValueFrom(this.api.getTerminatedWithBalance());
      this.rows.set(queue.rows);
      this.totals.set(queue.totals);
    } catch {
      this.error.set('ERRORS.GENERIC');
      this.rows.set([]);
      this.totals.set([]);
    } finally {
      this.loading.set(false);
    }
  }
}
